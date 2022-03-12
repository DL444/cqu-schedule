using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
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
            string initialCookies = cookieContainer?.GetCookieHeader(message.RequestUri);
            if (initialCookies != null && initialCookies.Length > 0)
            {
                message.Headers.Add("Cookie", new[] { initialCookies });
            }
            HttpResponseMessage response = await httpClient.SendAsync(message);
            if (response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string> cookies))
            {
                foreach (string cookie in cookies)
                {
                    cookieContainer.SetCookies(response.RequestMessage.RequestUri, cookie);
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
                string cookieHeader = cookieContainer.GetCookieHeader(request.RequestUri);
                if (!string.IsNullOrEmpty(cookieHeader))
                {
                    request.Headers.Add("Cookie", new[] { cookieHeader });
                }
                response = await httpClient.SendAsync(request);
                if (response.Headers.TryGetValues("Set-Cookie", out cookies))
                {
                    foreach (string cookie in cookies)
                    {
                        cookieContainer.SetCookies(response.RequestMessage.RequestUri, cookie);
                    }
                }
                redirects++;
            }
            return response;
        }
    }
}
