using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DL444.CquSchedule.Backend.Exceptions;
using DL444.CquSchedule.Backend.Models;

namespace DL444.CquSchedule.Backend.Services
{
    internal interface IScheduleService
    {
        Task<string> SignInAsync(string username, string password);
        Task<Schedule> GetScheduleAsync(string username, string token);
        Task<Term> GetTermAsync(string token, TimeSpan offset);
    }

    internal class ScheduleService : IScheduleService
    {
        public ScheduleService(HttpClient httpClient, IUpstreamCredentialEncryptionService encryptionService)
        {
            this.httpClient = httpClient;
            this.encryptionService = encryptionService;
        }

        public async Task<string> SignInAsync(string username, string password)
        {
            CookieContainer cookieContainer = new CookieContainer();
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "http://my.cqu.edu.cn/authserver/casLogin?redirect_uri=http%3A%2F%2Fmy.cqu.edu.cn%2Fenroll%2Fcas");
            HttpResponseMessage response = await SendRequestFollowingRedirectsAsync(request, cookieContainer);
            string body = await response.Content.ReadAsStringAsync();
            SigninInfo info = GetSigninInfo(body);
            string encryptedPassword = await encryptionService.EncryptAsync(password, info.Key);

            request = new HttpRequestMessage(HttpMethod.Post, "http://authserver.cqu.edu.cn/authserver/login?service=http://my.cqu.edu.cn/authserver/authentication/cas");
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("username", username),
                new KeyValuePair<string, string>("password", encryptedPassword),
                new KeyValuePair<string, string>("lt", info.Lt),
                new KeyValuePair<string, string>("dllt", "userNamePasswordLogin"),
                new KeyValuePair<string, string>("execution", info.Execution),
                new KeyValuePair<string, string>("_eventId", "submit"),
                new KeyValuePair<string, string>("rmShown", "1")
            });
            request.Headers.Add("Cookie", cookieContainer.GetCookies(request.RequestUri));
            response = await SendRequestFollowingRedirectsAsync(request, cookieContainer);
            if (response.RequestMessage.RequestUri.Authority == "authserver.cqu.edu.cn")
            {
                AuthenticationException ex = new AuthenticationException("Failed to authenticate user. Invalid credentials or captcha required.")
                {
                    Result = AuthenticationResult.UnknownFailure
                };
                string responseContent = await response.Content.ReadAsStringAsync();
                Regex errorRegex = new Regex("<span id=\"msg\" class=\"login_auth_error\">(.*?)</span>");
                Match errorMatch = errorRegex.Match(responseContent);
                if (errorMatch.Success)
                {
                    string errorDescription = errorMatch.Groups[1].Value;
                    ex.ErrorDescription = errorDescription;
                    if (errorDescription.Contains("密码", StringComparison.Ordinal) || errorDescription.Contains("password", StringComparison.Ordinal))
                    {
                        ex.Result = AuthenticationResult.IncorrectCredential;
                    }
                    else if (errorDescription.Contains("验证码", StringComparison.Ordinal) || errorDescription.Contains("verification", StringComparison.Ordinal))
                    {
                        ex.Result = AuthenticationResult.CaptchaRequired;
                    }
                }
                throw ex;
            }

            request = new HttpRequestMessage(HttpMethod.Get, "http://my.cqu.edu.cn/authserver/oauth/authorize?client_id=enroll-prod&response_type=code&scope=all&state=&redirect_uri=http%3A%2F%2Fmy.cqu.edu.cn%2Fenroll%2Ftoken-index");
            request.Headers.Add("Cookie", cookieContainer.GetCookies(request.RequestUri));
            response = await SendRequestFollowingRedirectsAsync(request, cookieContainer);
            Regex regex = new Regex("code=(.{6})");
            string code = regex.Match(response.RequestMessage.RequestUri.ToString()).Groups[1].Value;

            request = new HttpRequestMessage(HttpMethod.Post, "http://my.cqu.edu.cn/authserver/oauth/token");
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", "enroll-prod"),
                new KeyValuePair<string, string>("client_secret", "app-a-1234"),
                new KeyValuePair<string, string>("code", code),
                new KeyValuePair<string, string>("redirect_uri", "http://my.cqu.edu.cn/enroll/token-index"),
                new KeyValuePair<string, string>("grant_type", "authorization_code")
            });
            request.Headers.Add("Cookie", cookieContainer.GetCookies(request.RequestUri));
            response = await SendRequestFollowingRedirectsAsync(request, cookieContainer);

            regex = new Regex("\"access_token\":\"(.*?)\"");
            Match tokenMatch = regex.Match(await response.Content.ReadAsStringAsync());
            if (!tokenMatch.Success)
            {
                throw new FormatException("Server did not return a token.");
            }
            return tokenMatch.Groups[1].Value;
        }

        public async Task<Schedule> GetScheduleAsync(string username, string token)
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, $"http://my.cqu.edu.cn/enroll-api/timetable/student/{username}");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            HttpResponseMessage response = await httpClient.SendAsync(request);
            var responseModel = await JsonSerializer.DeserializeAsync<ScheduleResponseModel>(await response.Content.ReadAsStreamAsync());
            Schedule schedule = new Schedule(username);
            if (!responseModel.Status.Equals("success", StringComparison.Ordinal) || responseModel.Data == null)
            {
                throw new UpstreamRequestException("Upstream server did not return success status for schedule request.")
                {
                    ErrorDescription = responseModel.Message
                };
            }
            foreach (var responseEntry in responseModel.Data)
            {
                if (responseEntry.Weeks == null || responseEntry.DayOfWeek == null || responseEntry.Session == null)
                {
                    continue;
                }
                string lecturer = responseEntry.Lecturers == null ? string.Empty : responseEntry.Lecturers.FirstOrDefault().Lecturer;
                ScheduleEntry entry = new ScheduleEntry()
                {
                    Name = responseEntry.Name,
                    Lecturer = lecturer,
                    Room = responseEntry.Room,
                    DayOfWeek = int.Parse(responseEntry.DayOfWeek),
                    StartSession = responseEntry.Session.IndexOf('1') + 1,
                    EndSession = responseEntry.Session.LastIndexOf('1') + 1
                };
                for (int i = 0; i < responseEntry.Weeks.Length; i++)
                {
                    if (responseEntry.Weeks[i] == '1')
                    {
                        schedule.AddEntry(i + 1, entry);
                    }
                }
            }
            schedule.Weeks.Sort((x, y) => x.WeekNumber.CompareTo(y.WeekNumber));
            return schedule;
        }

        public async Task<Term> GetTermAsync(string token, TimeSpan offset)
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "http://my.cqu.edu.cn/resource-api/session/cur-timetable-session");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            HttpResponseMessage response = await httpClient.SendAsync(request);
            var responseModel = await JsonSerializer.DeserializeAsync<TermResponseModel>(await response.Content.ReadAsStreamAsync());
            if (!responseModel.Status.Equals("success", StringComparison.Ordinal))
            {
                throw new UpstreamRequestException("Upstream server did not return success status for term request.")
                {
                    ErrorDescription = responseModel.Message
                };
            }
            return new Term()
            {
                StartDate = new DateTimeOffset(DateTime.Parse(responseModel.Data.StartDateString), offset),
                EndDate = new DateTimeOffset(DateTime.Parse(responseModel.Data.EndDateString), offset).AddDays(1)
            };
        }

        private async Task<HttpResponseMessage> SendRequestFollowingRedirectsAsync(HttpRequestMessage message, CookieContainer cookieContainer, int maxRedirects = 10)
        {
            int redirects = 0;
            HttpResponseMessage response = await httpClient.SendAsync(message);
            if (response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string> cookies))
            {
                foreach (string cookie in cookies)
                {
                    cookieContainer.SetCookie(cookie, response.RequestMessage.RequestUri);
                }
            }
            while (response.StatusCode == System.Net.HttpStatusCode.Redirect && redirects < maxRedirects)
            {
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, response.Headers.Location);
                request.Headers.Add("Cookie", new[] { cookieContainer.GetCookies(request.RequestUri) });
                response = await httpClient.SendAsync(request);
                if (response.Headers.TryGetValues("Set-Cookie", out cookies))
                {
                    foreach (string cookie in cookies)
                    {
                        cookieContainer.SetCookie(cookie, response.RequestMessage.RequestUri);
                    }
                }
                redirects++;
            }
            return response;
        }

        private static SigninInfo GetSigninInfo(string html)
        {
            Regex regex = new Regex("input type=\"hidden\" name=\"lt\" value=\"(.*?)\"");
            Match match = regex.Match(html);
            if (!match.Success)
            {
                throw new ArgumentException("Supplied HTML is not valid. Lt not found.");
            }
            string lt = match.Groups[1].Value;

            regex = new Regex("input type=\"hidden\" name=\"execution\" value=\"(.*?)\"");
            match = regex.Match(html);
            if (!match.Success)
            {
                throw new ArgumentException("Supplied HTML is not valid. Execution not found.");
            }
            string exec = match.Groups[1].Value;

            regex = new Regex("var pwdDefaultEncryptSalt = \"(.*?)\"");
            match = regex.Match(html);
            if (!match.Success)
            {
                throw new ArgumentException("Supplied HTML is not valid. Default salt not found.");
            }
            string key = match.Groups[1].Value;

            return new SigninInfo()
            {
                Lt = lt,
                Execution = exec,
                Key = key
            };
        }

        private struct SigninInfo
        {
            public string Lt { get; set; }
            public string Execution { get; set; }
            public string Key { get; set; }
        }

        private struct ScheduleResponseModel
        {
            [JsonPropertyName("status")]
            public string Status { get; set; }
            [JsonPropertyName("msg")]
            public string Message { get; set; }
            [JsonPropertyName("data")]
            public ScheduleDataEntry[] Data { get; set; }
        }

        private struct ScheduleDataEntry
        {
            [JsonPropertyName("courseName")]
            public string Name { get; set; }
            [JsonPropertyName("roomName")]
            public string Room { get; set; }
            [JsonPropertyName("teachingWeek")]
            public string Weeks { get; set; }
            [JsonPropertyName("weekDay")]
            public string DayOfWeek { get; set; }
            [JsonPropertyName("period")]
            public string Session { get; set; }
            [JsonPropertyName("classTimetableInstrVOList")]
            public LecturerEntry[] Lecturers { get; set; }
        }

        private struct LecturerEntry
        {
            [JsonPropertyName("instructorName")]
            public string Lecturer { get; set; }
        }

        private struct TermResponseModel
        {
            [JsonPropertyName("status")]
            public string Status { get; set; }
            [JsonPropertyName("msg")]
            public string Message { get; set; }
            [JsonPropertyName("data")]
            public TermDataModel Data { get; set; }
        }

        private struct TermDataModel
        {
            [JsonPropertyName("beginDate")]
            public string StartDateString { get; set; }
            [JsonPropertyName("endDate")]
            public string EndDateString { get; set; }
            [JsonPropertyName("year")]
            public string Year { get; set; }
            [JsonPropertyName("term")]
            public string Term { get; set; }
        }

        private class CookieContainer
        {
            public string GetCookies(string uri) => GetCookies(new Uri(uri));

            public string GetCookies(Uri uri)
            {
                string domain = uri.Host.ToUpperInvariant();
                string path = uri.AbsolutePath.ToUpperInvariant();
                StringBuilder cookieBuilder = new StringBuilder();
                foreach (CookieDomain cookieDomain in container.Keys)
                {
                    if (domain.EndsWith(cookieDomain.Domain, StringComparison.Ordinal) && path.StartsWith(cookieDomain.Path, StringComparison.Ordinal))
                    {
                        Dictionary<string, string> subcontainer = container[cookieDomain];
                        foreach (KeyValuePair<string, string> cookie in subcontainer)
                        {
                            cookieBuilder.Append($"{(cookieBuilder.Length > 0 ? "; " : string.Empty)}{cookie.Key}={cookie.Value}");
                        }
                    }
                }
                return cookieBuilder.ToString();
            }

            public void SetCookie(string cookie, Uri defaultUri)
            {
                string domain = defaultUri.Host;
                string path = defaultUri.AbsolutePath;
                string[] parameters = cookie.Split(';');
                if (parameters.Length == 0)
                {
                    return;
                }

                Regex paramRegex = new Regex("^(.*?)=(.*?)$");
                Match match = paramRegex.Match(parameters[0].Trim());
                if (!match.Success)
                {
                    return;
                }
                string key = match.Groups[1].Value;
                string value = match.Groups[2].Value;

                foreach (string parameter in parameters.Skip(1))
                {
                    match = paramRegex.Match(parameter.Trim());
                    if (match.Success)
                    {
                        switch (match.Groups[1].Value.ToUpperInvariant())
                        {
                            case "DOMAIN":
                                domain = match.Groups[2].Value;
                                break;
                            case "PATH":
                                path = match.Groups[2].Value;
                                break;
                        }
                    }
                }

                CookieDomain cookieDomain = new CookieDomain()
                {
                    Domain = domain.ToUpperInvariant(),
                    Path = path.ToUpperInvariant()
                };
                if (!container.ContainsKey(cookieDomain))
                {
                    container.Add(cookieDomain, new Dictionary<string, string>());
                }
                Dictionary<string, string> subcontainer = container[cookieDomain];
                if (subcontainer.ContainsKey(key))
                {
                    subcontainer[key] = value;
                }
                else
                {
                    subcontainer.Add(key, value);
                }
            }

            private readonly Dictionary<CookieDomain, Dictionary<string, string>> container = new Dictionary<CookieDomain, Dictionary<string, string>>();
        }

        private struct CookieDomain : IEquatable<CookieDomain>
        {
            public string Domain { get; set; }
            public string Path { get; set; }

            public override bool Equals(object obj) => obj is CookieDomain domain && Equals(domain);
            public bool Equals(CookieDomain domain) => Domain.Equals(domain.Domain, StringComparison.Ordinal) && Path.Equals(domain.Path, StringComparison.Ordinal);
            public override int GetHashCode() => $"{Domain}-{Path}".GetHashCode();
        }

        private readonly HttpClient httpClient;
        private readonly IUpstreamCredentialEncryptionService encryptionService;
    }
}
