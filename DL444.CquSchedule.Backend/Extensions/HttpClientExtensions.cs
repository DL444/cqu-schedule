using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DL444.CquSchedule.Backend.Extensions
{
    internal static class HttpClientExtensions
    {
        public static async Task<HttpResponseMessage> SendRequestFollowingRedirectsAsync(
            this HttpClient httpClient,
            HttpRequestMessage message,
            CookieContainer cookieContainer,
            HashSet<string> breakoutUris = null,
            int maxRedirects = 10)
        {
            int redirects = 0;
            string initialCookies = cookieContainer?.GetCookies(message.RequestUri);
            if (initialCookies != null && initialCookies.Length > 0)
            {
                message.Headers.Add("Cookie", new[] { initialCookies });
            }
            HttpResponseMessage response = await httpClient.SendAsync(message);
            if (response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string> cookies))
            {
                foreach (string cookie in cookies)
                {
                    cookieContainer.SetCookie(cookie, response.RequestMessage.RequestUri);
                }
            }
            while ((response.StatusCode == System.Net.HttpStatusCode.Redirect || response.StatusCode == System.Net.HttpStatusCode.Moved) && redirects < maxRedirects)
            {
                Uri location = response.Headers.Location;
                if (breakoutUris != null && breakoutUris.Contains(location.GetLeftPart(UriPartial.Path).ToUpperInvariant()))
                {
                    break;
                }
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, location);
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
    }

    internal sealed class CookieContainer
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

    internal struct CookieDomain : IEquatable<CookieDomain>
    {
        public string Domain { get; set; }
        public string Path { get; set; }

        public override bool Equals(object obj) => obj is CookieDomain domain && Equals(domain);
        public bool Equals(CookieDomain domain) => Domain.Equals(domain.Domain, StringComparison.Ordinal) && Path.Equals(domain.Path, StringComparison.Ordinal);
        public override int GetHashCode() => $"{Domain}-{Path}".GetHashCode();
    }
}
