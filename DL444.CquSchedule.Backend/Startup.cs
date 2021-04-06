using System;
using System.Net.Http;
using System.Text;
using Azure.Cosmos;
using Azure.Identity;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
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
            var config = builder.GetContext().Configuration;

            string connection = config.GetValue<string>("Database:Connection");
            string database = config.GetValue<string>("Database:Database");
            string container = config.GetValue<string>("Database:Container");
            string keyVaultUri = config.GetValue<string>("Credential:KeyVault");
            string keyName = config.GetValue<string>("Credential:KeyName");
            builder.Services.AddSingleton(_ => new CosmosClient(connection).GetContainer(database, container));
            builder.Services.AddSingleton(x =>
            {
                var credential = new DefaultAzureCredential();
                Uri latestKeyId = new KeyClient(new Uri(keyVaultUri), credential).GetKey(keyName).Value.Id;
                return new CryptographyClient(latestKeyId, credential);
            });
            builder.Services.AddSingleton<IWellknownDataService, WellknownDataService>();
            builder.Services.AddSingleton<ILocalizationService, LocalizationService>();

            int timeout = config.GetValue("Upstream:Timeout", 30);
            builder.Services.AddHttpClient<IScheduleService, ScheduleService>(x => x.Timeout = TimeSpan.FromSeconds(timeout))
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler()
                {
                    UseCookies = false,
                    AllowAutoRedirect = false
                });

            builder.Services.AddTransient<IUpstreamCredentialEncryptionService, UpstreamCredentialEncryptionService>();
            builder.Services.AddTransient<IStoredCredentialEncryptionService, KeyVaultCredentialEncryptionService>();
            builder.Services.AddTransient<IDataService, DataService>();
            builder.Services.AddTransient<ITermService, TermService>();
            builder.Services.AddTransient<ICalendarService, CalendarService>();

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        public override void ConfigureAppConfiguration(IFunctionsConfigurationBuilder builder)
        {
            string rootPath = builder.GetContext().ApplicationRootPath;
            builder.ConfigurationBuilder
                .AddJsonFile(System.IO.Path.Combine(rootPath, "local.settings.json"), true)
                .AddJsonFile(System.IO.Path.Combine(rootPath, "localization.json"))
                .AddJsonFile(System.IO.Path.Combine(rootPath, "wellknown.json"))
                .AddEnvironmentVariables()
                .Build();
        }
    }
}
