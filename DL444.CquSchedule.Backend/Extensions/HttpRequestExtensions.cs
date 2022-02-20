using System.Text.Json;
using System.Threading.Tasks;
using DL444.CquSchedule.Backend.Models;
using DL444.CquSchedule.Models;
using Microsoft.AspNetCore.Http;

namespace DL444.CquSchedule.Backend.Extensions
{
    internal static class HttpRequestExtensions
    {
        public static async Task<Credential> GetCredentialAsync(this HttpRequest request)
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
    }
}
