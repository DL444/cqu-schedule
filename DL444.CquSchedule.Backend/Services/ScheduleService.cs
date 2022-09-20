using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DL444.CquSchedule.Backend.Exceptions;
using DL444.CquSchedule.Backend.Extensions;
using DL444.CquSchedule.Backend.Models;
using Microsoft.Extensions.Configuration;

namespace DL444.CquSchedule.Backend.Services
{
    internal interface IScheduleService
    {
        bool SupportsMultiterm { get; }
        Task<ISignInContext> SignInAsync(string username, string password);
        Task<Schedule> GetScheduleAsync(string username, string termId, ISignInContext signInContext, TimeSpan offset);
        Task<Term> GetTermAsync(ISignInContext signInContext, TimeSpan offset);
    }

    internal sealed class UndergraduateScheduleService : IScheduleService
    {
        public UndergraduateScheduleService(HttpClient httpClient, IUpstreamCredentialEncryptionService encryptionService, IExamStudentIdService examStudentIdService, IConfiguration config)
        {
            this.httpClient = httpClient;
            this.encryptionService = encryptionService;
            this.examStudentIdService = examStudentIdService;
            vacationServeDays = config.GetValue("Calendar:VacationServeDays", 3);
        }

        public bool SupportsMultiterm => true;

        public async Task<ISignInContext> SignInAsync(string username, string password)
        {
            CookieContainer cookieContainer = new CookieContainer();

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "https://my.cqu.edu.cn/authserver/casLogin?redirect_uri=https://my.cqu.edu.cn/enroll/cas");
            HttpResponseMessage response = await httpClient.SendRequestFollowingRedirectsAsync(request, cookieContainer);
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                throw new AuthenticationException("Failed to authenticate user. First page access forbidden.")
                {
                    Result = AuthenticationResult.ConnectionFailed
                };
            }

            string body = await response.Content.ReadAsStringAsync();
            SigninInfo info = GetSigninInfo(body);
            string encryptedPassword = encryptionService.Encrypt(password, info.Crypto);

            request = new HttpRequestMessage(HttpMethod.Post, "https://sso.cqu.edu.cn/login");
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("username", username),
                new KeyValuePair<string, string>("password", encryptedPassword),
                new KeyValuePair<string, string>("type", "UsernamePassword"),
                new KeyValuePair<string, string>("execution", info.Execution),
                new KeyValuePair<string, string>("_eventId", "submit"),
                new KeyValuePair<string, string>("croypto", info.Crypto)
            });

            try
            {
                response = await httpClient.SendRequestFollowingRedirectsAsync(request, cookieContainer, new HashSet<string>()
                {
                    "HTTP://AUTHSERVER.CQU.EDU.CN/AUTHSERVER/IMPROVEINFO.DO",
                    "HTTP://MY.CQU.EDU.CN/",
                    "HTTPS://MY.CQU.EDU.CN/"
                });
            }
            catch (SocketException ex)
            {
                AuthenticationException authEx = new AuthenticationException("Failed to authenticate user. Server closed connection.", ex)
                {
                    Result = AuthenticationResult.UnknownFailure,
                };
                throw authEx;
            }

            if (!response.IsSuccessStatusCode)
            {
                AuthenticationException ex = new AuthenticationException("Failed to authenticate user. Invalid credentials or captcha required.")
                {
                    Result = AuthenticationResult.UnknownFailure
                };
                string responseContent = await response.Content.ReadAsStringAsync();
                Regex errorRegex = new Regex("<div.*id=\"login-error-msg\">\\n\\s*?<span>(.*?)</span>", RegexOptions.CultureInvariant);
                Match errorMatch = errorRegex.Match(responseContent);
                if (errorMatch.Success)
                {
                    string errorCode = errorMatch.Groups[1].Value;
                    ex.ErrorDescription = errorCode;
                    if (errorCode.Equals("1030027", StringComparison.Ordinal))
                    {
                        ex.Result = AuthenticationResult.IncorrectCredential;
                    }
                    else if (errorCode.Equals("1030031", StringComparison.Ordinal))
                    {
                        ex.Result = AuthenticationResult.IncorrectCredential;
                    }
                    else if (errorCode.Equals("1410041", StringComparison.Ordinal))
                    {
                        ex.Result = AuthenticationResult.IncorrectCredential;
                    }
                    else if (errorCode.Equals("1410040", StringComparison.Ordinal))
                    {
                        ex.Result = AuthenticationResult.IncorrectCredential;
                    }
                }
                throw ex;
            }

            request = new HttpRequestMessage(HttpMethod.Get, "https://my.cqu.edu.cn/authserver/oauth/authorize?client_id=enroll-prod&response_type=code&scope=all&state=&redirect_uri=https%3A%2F%2Fmy.cqu.edu.cn%2Fenroll%2Ftoken-index");
            response = await httpClient.SendRequestFollowingRedirectsAsync(request, cookieContainer);
            Regex regex = new Regex("code=(.{6})");
            string code = regex.Match(response.RequestMessage.RequestUri.ToString()).Groups[1].Value;

            request = new HttpRequestMessage(HttpMethod.Post, "https://my.cqu.edu.cn/authserver/oauth/token");
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", "enroll-prod"),
                new KeyValuePair<string, string>("client_secret", "app-a-1234"),
                new KeyValuePair<string, string>("code", code),
                new KeyValuePair<string, string>("redirect_uri", "https://my.cqu.edu.cn/enroll/token-index"),
                new KeyValuePair<string, string>("grant_type", "authorization_code")
            });
            response = await httpClient.SendRequestFollowingRedirectsAsync(request, cookieContainer);

            regex = new Regex("\"access_token\":\"(.*?)\"");
            Match tokenMatch = regex.Match(await response.Content.ReadAsStringAsync());
            if (!tokenMatch.Success)
            {
                AuthenticationException authEx = new AuthenticationException("Failed to authenticate user. Server did not return a token.")
                {
                    Result = AuthenticationResult.UnknownFailure,
                };
                throw authEx;
            }
            return new UndergraduateSignInContext()
            {
                Token = tokenMatch.Groups[1].Value
            };
        }

        public async Task<Schedule> GetScheduleAsync(string username, string termId, ISignInContext signInContext, TimeSpan offset)
        {
            string token;
            if (signInContext is UndergraduateSignInContext undergradSignInContext)
            {
                if (undergradSignInContext.IsValid)
                {
                    token = undergradSignInContext.Token;
                }
                else
                {
                    throw new ArgumentException("Sign in context is invalid.");
                }
            }
            else if (signInContext == null)
            {
                throw new ArgumentNullException(nameof(signInContext), "Sign in context is null.");
            }
            else
            {
                throw new ArgumentException("Sign in context is not for undergraduate.");
            }

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "https://my.cqu.edu.cn/api/enrollment/enrollment-batch/user-switch-batch");
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("sessionId", termId)
            });
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            HttpResponseMessage response = await httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            string examStudentId = examStudentIdService.GetExamStudentId(username);
            Task<HttpResponseMessage> examTask = httpClient.GetAsync($"https://my.cqu.edu.cn/api/exam/examTask/get-student-exam-list-outside?studentId={examStudentId}");

            request = new HttpRequestMessage(HttpMethod.Get, $"https://my.cqu.edu.cn/api/enrollment/timetable/student/{username}");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            response = await httpClient.SendAsync(request);
            var responseModel = await UpstreamScheduleResponseModelSerializerContext.Default.DeserializeFromStringAsync(await response.Content.ReadAsStreamAsync());
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
                string name = responseEntry.Name;
                if (expClassTypes.Contains(responseEntry.ClassType))
                {
                    name = name.Contains("实验") ? name : $"{name}实验";
                }
                string lecturer = responseEntry.Lecturers == null ? string.Empty : responseEntry.Lecturers.FirstOrDefault().Lecturer;
                ScheduleEntry entry = new ScheduleEntry()
                {
                    Name = name,
                    Lecturer = lecturer,
                    Room = responseEntry.Room,
                    SimplifiedRoom = GetSimplifiedRoom(responseEntry.Room),
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

            response = await examTask;
            var examResponseModel = await UpstreamExamResponseModelSerializerContext.Default.DeserializeFromStringAsync(await response.Content.ReadAsStreamAsync());
            if (!examResponseModel.Status.Equals("success", StringComparison.Ordinal) || examResponseModel.Data.Content == null)
            {
                throw new UpstreamRequestException("Upstream server did not return success status for exam request.")
                {
                    ErrorDescription = examResponseModel.Message
                };
            }
            if (examResponseModel.Data.TotalPages > 1)
            {
                throw new UpstreamRequestException("Upstream server returned multiple exam pages.");
            }
            foreach (var responseEntry in examResponseModel.Data.Content)
            {
                bool dateParseSuccess = DateTime.TryParse(responseEntry.Date, out DateTime localDate);
                bool startTimeParseSuccess = DateTime.TryParse(responseEntry.StartTime, out DateTime localStartTimeInDay);
                bool endTimeParseSuccess = DateTime.TryParse(responseEntry.EndTime, out DateTime localEndTimeInDay);
                if (!dateParseSuccess || !startTimeParseSuccess || !endTimeParseSuccess || localEndTimeInDay < localStartTimeInDay)
                {
                    continue;
                }
                DateTime localStartTime = new DateTime(localDate.Year, localDate.Month, localDate.Day, localStartTimeInDay.Hour, localStartTimeInDay.Minute, localStartTimeInDay.Second);
                DateTime localEndTime = new DateTime(localDate.Year, localDate.Month, localDate.Day, localEndTimeInDay.Hour, localEndTimeInDay.Minute, localEndTimeInDay.Second);
                DateTimeOffset startTime = new DateTimeOffset(localStartTime, offset);
                DateTimeOffset endTime = new DateTimeOffset(localEndTime, offset);

                bool seatParseSuccess = int.TryParse(responseEntry.Seat, out int seat);
                ExamEntry entry = new ExamEntry
                {
                    Name = $"{responseEntry.Name}考试",
                    Room = responseEntry.Room,
                    SimplifiedRoom = GetSimplifiedRoom(responseEntry.Room),
                    Seat = seatParseSuccess ? seat : 0,
                    StartTime = startTime,
                    EndTime = endTime
                };
                schedule.Exams.Add(entry);
            }
            schedule.Exams.Sort((x, y) => x.StartTime.CompareTo(y.StartTime));

            return schedule;
        }

        public async Task<Term> GetTermAsync(ISignInContext signInContext, TimeSpan offset)
        {
            string token;
            if (signInContext is UndergraduateSignInContext undergradSignInContext)
            {
                if (undergradSignInContext.IsValid)
                {
                    token = undergradSignInContext.Token;
                }
                else
                {
                    throw new ArgumentException("Sign in context is invalid.");
                }
            }
            else if (signInContext == null)
            {
                throw new ArgumentNullException(nameof(signInContext), "Sign in context is null.");
            }
            else
            {
                throw new ArgumentException("Sign in context is not for undergraduate.");
            }

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "https://my.cqu.edu.cn/api/resourceapi/session/info-detail");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            HttpResponseMessage response = await httpClient.SendAsync(request);
            var termListResponseModel = await UpstreamTermListResponseModelSerializerContext.Default.DeserializeFromStringAsync(await response.Content.ReadAsStreamAsync());

            (int prevHint, Term prevTerm) = await GetCandidateTermAsync(token, termListResponseModel.CurrentTerm, offset);
            if (prevHint == 0)
            {
                return prevTerm;
            }

            int index = termListResponseModel.Terms.FindIndex(x => x.Id.Equals(termListResponseModel.CurrentTerm, StringComparison.Ordinal)) - prevHint;
            while (index >= 0 && index < termListResponseModel.Terms.Count)
            {
                (int hint, Term term) = await GetCandidateTermAsync(token, termListResponseModel.Terms[index].Id, offset);
                if (hint == 0)
                {
                    return term;
                }

                if (prevHint * hint > 0)
                {
                    index -= hint;
                    prevHint = hint;
                    prevTerm = term;
                }
                else if (prevHint > 0)
                {
                    return term;
                }
                else if (prevHint < 0)
                {
                    return prevTerm;
                }
            }
            return prevTerm;
        }

        private async Task<(int hint, Term term)> GetCandidateTermAsync(string token, string termId, TimeSpan offset)
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, $"https://my.cqu.edu.cn/api/resourceapi/session/info/{termId}");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            HttpResponseMessage response = await httpClient.SendAsync(request);
            var responseModel = await UpstreamTermResponseModelSerializerContext.Default.DeserializeFromStringAsync(await response.Content.ReadAsStreamAsync());
            if (!responseModel.Status.Equals("success", StringComparison.Ordinal))
            {
                throw new UpstreamRequestException("Upstream server did not return success status for term request.")
                {
                    ErrorDescription = responseModel.Message
                };
            }

            Term term = new Term()
            {
                SessionTermId = termId,
                StartDate = new DateTimeOffset(DateTime.Parse(responseModel.Data.StartDateString), offset),
                EndDate = new DateTimeOffset(DateTime.Parse(responseModel.Data.EndDateString), offset).AddDays(1)
            };
            int hint = 0;
            if (DateTimeOffset.Now < term.StartDate.AddDays(-vacationServeDays))
            {
                hint = -1;
            }
            else if (DateTimeOffset.Now > term.EndDate.AddDays(vacationServeDays))
            {
                hint = 1;
            }
            return (hint, term);
        }

        private static SigninInfo GetSigninInfo(string html)
        {
            Regex regex = new Regex("p id=\"login-page-flowkey\">(.*?)<");
            Match match = regex.Match(html);
            if (!match.Success)
            {
                throw new ArgumentException("Supplied HTML is not valid. Execution not found.");
            }
            string exec = match.Groups[1].Value;

            regex = new Regex("p id=\"login-croypto\">(.*?)<");
            match = regex.Match(html);
            if (!match.Success)
            {
                throw new ArgumentException("Supplied HTML is not valid. Crypto not found.");
            }
            string crypto = match.Groups[1].Value;

            return new SigninInfo()
            {
                Execution = exec,
                Crypto = crypto
            };
        }

        private static string GetSimplifiedRoom(string room)
        {
            if (room == null)
            {
                return null;
            }
            Match match = roomSimplifyRegex.Match(room);
            return match.Success ? match.Groups[2].Value : room;
        }

        private struct SigninInfo
        {
            public string Execution { get; set; }
            public string Crypto { get; set; }
        }

        private readonly HttpClient httpClient;
        private readonly IUpstreamCredentialEncryptionService encryptionService;
        private readonly IExamStudentIdService examStudentIdService;
        private readonly int vacationServeDays;
        private static readonly Regex roomSimplifyRegex = new Regex("(室|机房|中心|分析系统|创新设计|展示与分析).*?-(.*?)$");
        private static readonly string[] expClassTypes = new[] { "上机", "实验" };
    }

    internal sealed class PostgraduateScheduleService : IScheduleService
    {
        public PostgraduateScheduleService(HttpClient httpClient) => this.httpClient = httpClient;

        public bool SupportsMultiterm => false;

        public async Task<ISignInContext> SignInAsync(string username, string password)
        {
            CookieContainer cookieContainer = new CookieContainer();
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "http://mis.cqu.edu.cn/mis/login.jsp");
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("userId", username),
                new KeyValuePair<string, string>("password", password),
                new KeyValuePair<string, string>("userType", "student"),
            });
            HttpResponseMessage response;
            try
            {
                response = await httpClient.SendRequestFollowingRedirectsAsync(request, cookieContainer);
            }
            catch (SocketException ex)
            {
                AuthenticationException authEx = new AuthenticationException("Failed to authenticate user. Server closed connection.", ex)
                {
                    Result = AuthenticationResult.UnknownFailure,
                };
                throw authEx;
            }

            string body = await response.Content.ReadAsStringAsync();
            Regex resultRegex = new Regex("url=(.*?)\\.jsp");
            Match match = resultRegex.Match(body);
            AuthenticationResult authResult;
            string exceptionMsg;
            if (!match.Success)
            {
                authResult = AuthenticationResult.UnknownFailure;
                exceptionMsg = "Failed to authenticate user. Server did not return a authentication result.";
            }
            else
            {
                switch (match.Groups[1].Value.ToUpperInvariant())
                {
                    case "STUDENT":
                        authResult = AuthenticationResult.Success;
                        exceptionMsg = default;
                        break;
                    case "WRONGPWD":
                    case "NULLUSER":
                        authResult = AuthenticationResult.IncorrectCredential;
                        exceptionMsg = $"Failed to authenticate user. Reason: {match.Groups[1].Value}";
                        break;
                    default:
                        authResult = AuthenticationResult.UnknownFailure;
                        exceptionMsg = $"Failed to authenticate user. Reason: {match.Groups[1].Value}";
                        break;
                }
            }

            if (authResult != AuthenticationResult.Success)
            {
                AuthenticationException authEx = new AuthenticationException(exceptionMsg)
                {
                    Result = authResult,
                };
                throw authEx;
            }

            request = new HttpRequestMessage(HttpMethod.Get, "http://mis.cqu.edu.cn/mis/menu_student.jsp");
            response = await httpClient.SendRequestFollowingRedirectsAsync(request, cookieContainer);
            body = await response.Content.ReadAsStringAsync();
            Regex studentSerialRegex = new Regex("stuSerial=(.*?)\"");
            match = studentSerialRegex.Match(body);
            if (!match.Success)
            {
                AuthenticationException authEx = new AuthenticationException("Failed to authenticate user. Server did not return a student serial.")
                {
                    Result = AuthenticationResult.UnknownFailure,
                };
                throw authEx;
            }
            return new PostgraduateSignInContext()
            {
                StudentSerial = match.Groups[1].Value,
                CookieContainer = cookieContainer

            };
        }

        public async Task<Schedule> GetScheduleAsync(string username, string termId, ISignInContext signInContext, TimeSpan offset)
        {
            CookieContainer cookieContainer;
            string studentSerial;
            if (signInContext is PostgraduateSignInContext postgradSignInContext)
            {
                if (postgradSignInContext.IsValid)
                {
                    cookieContainer = postgradSignInContext.CookieContainer;
                    studentSerial = postgradSignInContext.StudentSerial;
                }
                else
                {
                    throw new ArgumentException("Sign in context is invalid.");
                }
            }
            else if (signInContext == null)
            {
                throw new ArgumentNullException(nameof(signInContext), "Sign in context is null.");
            }
            else
            {
                throw new ArgumentException("Sign in context is not for postgraduate.");
            }

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, $"http://mis.cqu.edu.cn/mis/curricula/show_stu.jsp?stuSerial={studentSerial}");
            HttpResponseMessage response = await httpClient.SendRequestFollowingRedirectsAsync(request, cookieContainer);
            string body = await response.Content.ReadAsStringAsync();

            Regex scheduleSlotRegex = new Regex("<td class=mode5>(<font color='red'>)?(.*?)(</font>)?</td>");
            MatchCollection scheduleSlotMatches = scheduleSlotRegex.Matches(body);
            if (scheduleSlotMatches.Count != 7 * 5)
            {
                throw new UpstreamRequestException($"Upstream server returned unexpected number of schedule slots. Expect 35, actually {scheduleSlotMatches.Count}.");
            }

            Regex scheduleItemRegex = new Regex("名称：(.*?)<br>周次：(.*?)周<br>节次：(.*?)<br>教师：(.*?)<br>(?:教室：(.*?)<br>)?(?:平台：(.*?)<br>)?.*<br>");
            Schedule schedule = new Schedule(username);
            for (int session = 0; session < 5; session++)
            {
                for (int dayOfWeek = 0; dayOfWeek < 7; dayOfWeek++)
                {
                    int index = session * 7 + dayOfWeek;
                    string cellValue = scheduleSlotMatches[index].Groups[2].Value;
                    if (cellValue.Equals("&nbsp;", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    MatchCollection scheduleItemMatches = scheduleItemRegex.Matches(cellValue);
                    if (scheduleItemMatches.Count == 0)
                    {
                        throw new UpstreamRequestException($"Upstream server returned unexpected cell value: \"{scheduleSlotMatches.Count}\".");
                    }
                    foreach (Match scheduleItemMatch in scheduleItemMatches)
                    {
                        string name = scheduleItemMatch.Groups[1].Value;
                        string weekNotation = scheduleItemMatch.Groups[2].Value;
                        string sessionNotation = scheduleItemMatch.Groups[3].Value;
                        string lecturer = scheduleItemMatch.Groups[4].Value;
                        string room = scheduleItemMatch.Groups[5].Value;
                        if (string.IsNullOrWhiteSpace(room))
                        {
                            room = scheduleItemMatch.Groups[6].Value;
                        }

                        (List<int> weeks, ScheduleEntry scheduleEntry) = GetScheduleEntry(name, weekNotation, dayOfWeek + 1, sessionNotation, lecturer, room);
                        weeks.ForEach(x => schedule.AddEntry(x, scheduleEntry));
                    }
                }
            }

            schedule.Weeks.Sort((x, y) => x.WeekNumber.CompareTo(y.WeekNumber));
            return schedule;
        }

        public Task<Term> GetTermAsync(ISignInContext signInContext, TimeSpan offset) => Task.FromResult<Term>(default);

        private static (List<int> weeks, ScheduleEntry entry) GetScheduleEntry(string name, string weekNotation, int dayOfWeek, string sessionNotation, string lecturer, string room)
        {
            ScheduleEntry scheduleEntry = new ScheduleEntry()
            {
                Name = name,
                DayOfWeek = dayOfWeek,
                Lecturer = lecturer,
                Room = room,
                SimplifiedRoom = room
            };
            (scheduleEntry.StartSession, scheduleEntry.EndSession) = ParseNumberSpanNotation(sessionNotation.Trim());

            List<int> parsedWeeks = new List<int>();
            string[] weekNotationFragments = weekNotation.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var weekNotationFragment in weekNotationFragments)
            {
                (int fragmentStart, int fragmentEnd) = ParseNumberSpanNotation(weekNotationFragment);
                parsedWeeks.AddRange(Enumerable.Range(fragmentStart, fragmentEnd - fragmentStart + 1));
            }

            return (parsedWeeks, scheduleEntry);
        }

        private static (int start, int end) ParseNumberSpanNotation(string numberSpanNotation)
        {
            bool isStandaloneNumber = int.TryParse(numberSpanNotation, out int number);
            if (isStandaloneNumber)
            {
                return (number, number);
            }
            Regex numberSpanRegex = new Regex("^(\\d.*)-(\\d.*)$");
            Match match = numberSpanRegex.Match(numberSpanNotation);
            if (!match.Success)
            {
                throw new ArgumentException($"Supplied string is not a valid number span notation: \"{numberSpanNotation}\"");
            }
            return (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value));
        }

        private readonly HttpClient httpClient;
    }
}
