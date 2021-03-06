using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using DL444.CquSchedule.Models;

namespace DL444.CquSchedule.Web.Services
{
    internal class ApiService
    {
        public ApiService(HttpClient httpClient) => this.httpClient = httpClient;

        public async Task<Response<IcsSubscription>> CreateSubscriptionAsync(Credential credential)
        {
            HttpResponseMessage response = await httpClient.PostAsJsonAsync("subscription", credential);
            Stream contentStream = await response.Content.ReadAsStreamAsync();
            return await JsonSerializer.DeserializeAsync<Response<IcsSubscription>>(contentStream, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true });
        }

        public async Task<Response<object>> DeleteSubscriptionAsync(Credential credential)
        {
            HttpResponseMessage response = await httpClient.PostAsJsonAsync("subscription/delete", credential);
            Stream contentStream = await response.Content.ReadAsStreamAsync();
            return await JsonSerializer.DeserializeAsync<Response<object>>(contentStream, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true });
        }

        private readonly HttpClient httpClient;
    }
}
