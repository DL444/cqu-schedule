using System;
using System.Net.Http;
using System.Threading.Tasks;
using DL444.CquSchedule.Web.Services;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DL444.CquSchedule.Web
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebAssemblyHostBuilder.CreateDefault(args);
            builder.RootComponents.Add<App>("#app");

            builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
            string baseAddress = builder.Configuration.GetValue<string>("ApiBaseAddress");
            builder.Services.AddSingleton(new ApiService(new HttpClient() { BaseAddress = new Uri(baseAddress) }));
            builder.Services.AddSingleton(new IcsContentContainerService());

            await builder.Build().RunAsync();
        }
    }
}
