using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure.Cosmos;
using Azure.Identity;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using DL444.CquSchedule.Backend.Models;
using DL444.CquSchedule.Backend.Services;
using Microsoft.Azure.EventGrid.Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace DL444.CquSchedule.Backend
{
    internal class CredentialKeyRotationFunction
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
        }

        [FunctionName("CredentialKeyRotation")]
        public Task StartRotationAsync([TimerTrigger("0 0 15 * * 0")] TimerInfo timer)
        {
            var options = new CreateRsaKeyOptions(keyName);
            options.KeyOperations.Add(KeyOperation.Encrypt);
            options.KeyOperations.Add(KeyOperation.Decrypt);
            options.ExpiresOn = DateTimeOffset.Now.AddDays(30);
            return keyClient.CreateRsaKeyAsync(options);
        }

        [FunctionName("PostCredentialKeyRotation_Orchestrator")]
        public async Task RunOrchestratorAsync(
            [OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var users = context.GetInput<List<string>>();
            var tasks = users.Select(user => context.CallActivityAsync("PostCredentialKeyRotation_Activity", user));
            await Task.WhenAll(tasks);
        }

        [FunctionName("PostCredentialKeyRotation_Activity")]
        public async Task RotateKeyAsync([ActivityTrigger] string username, ILogger log)
        {
            User user;
            try
            {
                user = await dataService.GetUserAsync(username);
            }
            catch (CosmosException ex)
            {
                log.LogError(ex, "Failed to fetch user from database. Status: {status}", ex.Status);
                return;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to fetch user from database.");
                return;
            }

            try
            {
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
                await dataService.SetUserAsync(user);
            }
            catch (CosmosException ex)
            {
                log.LogError(ex, "Failed to update user. Status: {status}", ex.Status);
                return;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to update user.");
                return;
            }
        }

        [FunctionName("PostCredentialKeyRotation_Client")]
        public async Task StartPostRotationAsync(
            [EventGridTrigger] EventGridEvent eventGridEvent,
            [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {
            if (!eventGridEvent.EventType.Equals("Microsoft.KeyVault.KeyNewVersionCreated", StringComparison.Ordinal))
            {
                log.LogError("Event with unsupported type received. Type {eventType}", eventGridEvent.EventType);
                return;
            }
            if (!(eventGridEvent.Data is JObject obj))
            {
                log.LogError("Event data is null.");
                return;
            }
            var data = obj.ToObject<EventData>();
            if (!data.ObjectName.Equals(keyName, StringComparison.Ordinal))
            {
                return;
            }

            clientContainerService.Client = new CryptographyClient(new Uri(data.Id), new DefaultAzureCredential());
            List<string> users = await dataService.GetUserIdsAsync();
            await starter.StartNewAsync("PostCredentialKeyRotation_Orchestrator", null, users);
        }

        private struct EventData
        {
            public string ObjectName { get; set; }
            public string Id { get; set; }
        }

        private readonly string keyName;
        private readonly KeyClient keyClient;
        private readonly ICryptographyClientContainerService clientContainerService;
        private readonly IStoredCredentialEncryptionService encryptionService;
        private readonly IDataService dataService;
    }
}
