using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using DL444.CquSchedule.Backend.Models;
using DL444.CquSchedule.Models;
using Microsoft.Azure.Functions.Worker.Http;

namespace DL444.CquSchedule.Backend.Extensions
{
    internal static class HttpRequestDataExtensions
    {
        public static async Task<Credential> GetCredentialAsync(this HttpRequestData request)
        {
            try
            {
                var serializerContext = new CredentialSerializerContext(new JsonSerializerOptions() { PropertyNameCaseInsensitive = true });
                Credential credential = await serializerContext.DeserializeFromStringAsync(request.Body);
                if (credential == null || string.IsNullOrWhiteSpace(credential.Username) || string.IsNullOrWhiteSpace(credential.Password))
                {
                    return default;
                }
                else
                {
                    return credential;
                }
            }
            catch (JsonException)
            {
                return default;
            }
        }

        public static HttpResponseData CreateStringContentResponse(this HttpRequestData request, HttpStatusCode statusCode, string content, string contentType)
        {
            HttpResponseData response = request.CreateResponse(statusCode);
            response.Headers.Add("Content-Type", contentType);
            response.WriteString(content);
            return response;
        }
    }
}
