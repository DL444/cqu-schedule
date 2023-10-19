using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Azure.Identity;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using DL444.CquSchedule.Backend.Extensions;
using DL444.CquSchedule.Backend.Services;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Contrib.WaitAndRetry;
using User = DL444.CquSchedule.Backend.Models.User;

namespace DL444.CquSchedule.Backend
{
    internal sealed class CredentialKeyRotationFunction
    {
        public CredentialKeyRotationFunction(
            IConfiguration config,
            KeyClient keyClient,
            ICryptographyClientContainerService clientContainerService,
            IStoredCredentialEncryptionService encryptionService,
            IDataService dataService)
        {
            keyName = config.GetValue<string>("Credential:KeyName");
            this.keyClient = keyClient;
            this.clientContainerService = clientContainerService;
            this.encryptionService = encryptionService;
            this.dataService = dataService;
            var backoff = Backoff.DecorrelatedJitterBackoffV2(TimeSpan.FromSeconds(5), 5);
            databaseBackoffPolicy = Policy
                .Handle<CosmosException>(x => x.StatusCode == HttpStatusCode.TooManyRequests)
                .WaitAndRetryAsync(backoff);
        }

        [Function("CredentialKeyRotation")]
        public Task StartRotationAsync([TimerTrigger("0 0 15 * * 0")] TimerInfo timer)
        {
            var options = new CreateRsaKeyOptions(keyName);
            options.KeyOperations.Add(KeyOperation.Encrypt);
            options.KeyOperations.Add(KeyOperation.Decrypt);
            options.ExpiresOn = DateTimeOffset.Now.AddDays(30);
            return keyClient.CreateRsaKeyAsync(options);
        }

        [Function("PostCredentialKeyRotation_Orchestrator")]
        public async Task RunOrchestratorAsync(
            [OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var users = context.GetInput<List<string>>();
            var tasks = users.Select(user => context.CallActivityAsync("PostCredentialKeyRotation_Activity", user));
            await Task.WhenAll(tasks);
        }

        [Function("PostCredentialKeyRotation_Activity")]
        public async Task RotateKeyAsync([ActivityTrigger] string username, FunctionContext ctx)
        {
            ILogger log = ctx.GetFunctionNamedLogger();
            User user;
            try
            {
                user = await databaseBackoffPolicy.ExecuteAsync(() => dataService.GetUserAsync(username));
            }
            catch (CosmosException ex)
            {
                log.LogError(ex, "Failed to fetch user from database. Status: {status}", ex.StatusCode);
                return;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to fetch user from database.");
                return;
            }

            try
            {
                user.LastRotatedPassword = user.Password;
                user.LastRotatedKeyId = user.KeyId;
                user = await encryptionService.DecryptAsync(user);
                user = await encryptionService.EncryptAsync(user);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to rotate user credential key.");
                return;
            }

            try
            {
                await databaseBackoffPolicy.ExecuteAsync(() => dataService.SetUserAsync(user));
            }
            catch (CosmosException ex)
            {
                log.LogError(ex, "Failed to update user. Status: {status}", ex.StatusCode);
                return;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to update user.");
                return;
            }
        }

        [Function("PostCredentialKeyRotation_Client")]
        public async Task StartPostRotationAsync(
            [EventGridTrigger] EventGridEvent eventGridEvent,
            [DurableClient] DurableTaskClient starter,
            FunctionContext ctx)
        {
            ILogger log = ctx.GetFunctionNamedLogger();
            if (!eventGridEvent.EventType.Equals("Microsoft.KeyVault.KeyNewVersionCreated", StringComparison.Ordinal))
            {
                log.LogError("Event with unsupported type received. Type {eventType}", eventGridEvent.EventType);
                return;
            }
            if (!string.Equals(eventGridEvent.Data.ObjectName, keyName, StringComparison.Ordinal))
            {
                return;
            }
            if (string.IsNullOrEmpty(eventGridEvent.Data.Id))
            {
                return;
            }
            clientContainerService.Client = new CryptographyClient(new Uri(eventGridEvent.Data.Id), new DefaultAzureCredential());
            List<string> users = await dataService.GetUserIdsAsync();
            await starter.ScheduleNewOrchestrationInstanceAsync("PostCredentialKeyRotation_Orchestrator", users);
        }

        internal sealed class EventGridEvent
        {
            public string EventType { get; set; }
            public EventData Data { get; set; }
        }

        internal struct EventData
        {
            public string ObjectName { get; set; }
            public string Id { get; set; }
        }

        private readonly string keyName;
        private readonly KeyClient keyClient;
        private readonly ICryptographyClientContainerService clientContainerService;
        private readonly IStoredCredentialEncryptionService encryptionService;
        private readonly IDataService dataService;
        private readonly IAsyncPolicy databaseBackoffPolicy;
    }
}
