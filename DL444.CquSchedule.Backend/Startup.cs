using System;
using System.Net.Http;
using System.Text;
using Azure.Identity;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using DL444.CquSchedule.Backend.Services;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
IHost host = new HostBuilder().ConfigureFunctionsWorkerDefaults().ConfigureAppConfiguration(ConfigureAppConfiguration).ConfigureServices(ConfigureServices).Build();
await host.RunAsync();

static void ConfigureServices(HostBuilderContext context, IServiceCollection services)
{
    services.AddSingleton(services =>
    {
        var config = services.GetService<IConfiguration>();
        string connection = config.GetValue<string>("Database:Connection");
        string database = config.GetValue<string>("Database:Database");
        string container = config.GetValue<string>("Database:Container");
        return new CosmosClient(connection).GetContainer(database, container);
    });
    services.AddSingleton<ICryptographyClientContainerService>(services =>
    {
        var config = services.GetService<IConfiguration>();
        string keyName = config.GetValue<string>("Credential:KeyName");
        Uri latestKeyId = services.GetService<KeyClient>().GetKey(keyName).Value.Id;
        var client = new CryptographyClient(latestKeyId, new DefaultAzureCredential());
        return new CryptographyClientContainerService()
        {
            Client = client
        };
    });
    services.AddSingleton<IWellknownDataService, WellknownDataService>();
    services.AddSingleton<ILocalizationService, LocalizationService>();

    var timeout = TimeSpan.FromSeconds(context.Configuration.GetValue("Upstream:Timeout", 30));
    // Pooled HttpMessageHandler via IHttpClientFactory is incompatible with automatic cookie management.
    // Manual cookie management required.
    // See https://docs.microsoft.com/en-us/aspnet/core/fundamentals/http-requests?view=aspnetcore-6.0#cookies for more info. 
    services.AddHttpClient<UndergraduateScheduleService>(x => x.Timeout = timeout)
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler()
        {
            UseCookies = false,
            AllowAutoRedirect = false
        });
    services.AddHttpClient<PostgraduateScheduleService>(x => x.Timeout = timeout)
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler()
        {
            UseCookies = false
        });

    services.AddTransient<IUpstreamCredentialEncryptionService, UpstreamCredentialEncryptionService>();
    services.AddTransient<IStoredCredentialEncryptionService, KeyVaultCredentialEncryptionService>();
    services.AddTransient<IDataService, DataService>();
    services.AddTransient<ITermService, TermService>();
    services.AddTransient<ICalendarService, CalendarService>();
    services.AddTransient(services =>
    {
        var config = services.GetService<IConfiguration>();
        string keyVaultUri = config.GetValue<string>("Credential:KeyVault");
        return new KeyClient(new Uri(keyVaultUri), new DefaultAzureCredential());
    });
}

static void ConfigureAppConfiguration(HostBuilderContext context, IConfigurationBuilder builder)
{
    string rootPath = context.HostingEnvironment.ContentRootPath;
    builder
        .AddJsonFile(System.IO.Path.Combine(rootPath, "local.settings.json"), true)
        .AddJsonFile(System.IO.Path.Combine(rootPath, "localization.json"))
        .AddJsonFile(System.IO.Path.Combine(rootPath, "wellknown.json"))
        .AddEnvironmentVariables();
}
