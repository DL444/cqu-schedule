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
            int requestCount = 0;
            HttpRequestMessage currentRequest = message;
            HttpResponseMessage currentResponse;
            do
            {
                string cookieHeader = cookieContainer.GetCookieHeader(currentRequest.RequestUri);
                if (!string.IsNullOrEmpty(cookieHeader))
                {
                    currentRequest.Headers.Add("Cookie", new[] { cookieHeader });
                }

                currentResponse = await httpClient.SendAsync(currentRequest);
                if (currentResponse.Headers.TryGetValues("Set-Cookie", out IEnumerable<string> cookies))
                {
                    foreach (string cookie in cookies)
                    {
                        cookieContainer.SetCookies(currentResponse.RequestMessage.RequestUri, cookie);
                    }
                }
                requestCount++;

                if ((currentResponse.StatusCode != HttpStatusCode.Redirect && currentResponse.StatusCode != HttpStatusCode.Moved) || requestCount > maxRedirects)
                {
                    break;
                }

                Uri location = currentResponse.Headers.Location;
                if (breakoutUris != null && breakoutUris.Contains(location.GetLeftPart(UriPartial.Path).ToUpperInvariant()))
                {
                    break;
                }
                currentRequest = new HttpRequestMessage(HttpMethod.Get, location);
            }
            while (true);

            return currentResponse;
        }
    }
}
