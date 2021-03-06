using System;
using System.Net.Http;
using System.Text;
using Azure.Cosmos;
using DL444.CquSchedule.Backend.Services;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[assembly: FunctionsStartup(typeof(DL444.CquSchedule.Backend.Startup))]
namespace DL444.CquSchedule.Backend
{
    internal class Startup : FunctionsStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            var context = builder.GetContext();
            var config = context.Configuration;

            string connection = config.GetValue<string>("Database:Connection");
            builder.Services.AddSingleton<CosmosClient>(_ => new CosmosClient(connection));
            builder.Services.AddSingleton<IWellknownDataService, WellknownDataService>();
            builder.Services.AddSingleton<ILocalizationService, LocalizationService>();

            int timeout = config.GetValue<int>("Upstream:Timeout", 30);
            builder.Services.AddHttpClient<IScheduleService, ScheduleService>(x => x.Timeout = TimeSpan.FromSeconds(timeout))
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler()
                {
                    UseCookies = false,
                    AllowAutoRedirect = false
                });

            builder.Services.AddTransient<IUpstreamCredentialEncryptionService, UpstreamCredentialEncryptionService>();
            builder.Services.AddTransient<IStoredCredentialEncryptionService, StoredCredentialEncryptionService>();
            builder.Services.AddTransient<IDataService, DataService>();
            builder.Services.AddTransient<ICalendarService, CalendarService>();

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        public override void ConfigureAppConfiguration(IFunctionsConfigurationBuilder builder)
        {
            var context = builder.GetContext();
            builder.ConfigurationBuilder
                .AddJsonFile(System.IO.Path.Combine(context.ApplicationRootPath, "local.settings.json"), true)
                .AddJsonFile(System.IO.Path.Combine(context.ApplicationRootPath, "localization.json"))
                .AddJsonFile(System.IO.Path.Combine(context.ApplicationRootPath, "wellknown.json"))
                .AddEnvironmentVariables()
                .Build();
        }
    }
}
