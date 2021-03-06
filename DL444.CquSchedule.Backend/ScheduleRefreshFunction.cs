using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.Cosmos;
using DL444.CquSchedule.Backend.Exceptions;
using DL444.CquSchedule.Backend.Models;
using DL444.CquSchedule.Backend.Services;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;

namespace DL444.CquSchedule.Backend
{
    internal class ScheduleRefreshFunction
    {
        public ScheduleRefreshFunction(IDataService dataService, IScheduleService scheduleService, IStoredCredentialEncryptionService encryptionService)
        {
            this.dataService = dataService;
            this.scheduleService = scheduleService;
            this.encryptionService = encryptionService;
        }

        [FunctionName("ScheduleRefresh_Orchestrator")]
        public async Task RunOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            var users = context.GetInput<List<string>>();
            foreach (var user in users)
            {
                await context.CallActivityAsync<string>("ScheduleRefresh_Activity", user);
            }
        }

        [FunctionName("ScheduleRefresh_Activity")]
        public async Task Refresh([ActivityTrigger] string username, ILogger log)
        {
            Task<Schedule> oldFetchTask = dataService.GetScheduleAsync(username);
            RecordStatus newStatus = RecordStatus.UpToDate;

            User user;
            try
            {
                user = await dataService.GetUserAsync(username);
                user = await encryptionService.DecryptAsync(user);
            }
            catch (CosmosException ex)
            {
                log.LogError(ex, "Failed to fetch user credential from database. Status: {status}", ex.Status);
                return;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to fetch user credential from database.");
                return;
            }

            Schedule newResource = default;
            try
            {
                string token = await scheduleService.SignInAsync(username, user.Password);
                newResource = await scheduleService.GetScheduleAsync(username, token);
            }
            catch (AuthenticationException ex)
            {
                if (ex.Reason == AuthenticationFailedReason.IncorrectCredential)
                {
                    newStatus = RecordStatus.StaleAuthError;
                }
                else
                {
                    log.LogError(ex, "User authentication failed. Probably captcha required.");
                    newStatus = RecordStatus.StaleUpstreamError;
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to fetch resource from upstream. Resource type {resourceType}", typeof(Schedule));
                newStatus = RecordStatus.StaleUpstreamError;
            }

            Schedule oldResource = default;
            try
            {
                oldResource = await oldFetchTask;
            }
            catch (CosmosException ex)
            {
                if (ex.Status != 404)
                {
                    log.LogError(ex, "Failed to fetch resource from database. Status: {status}", ex.Status);
                    return;
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to fetch resource from database.");
                return;
            }

            if (newResource.User == null && oldResource.User == null)
            {
                log.LogWarning("Update skipped because both old and new resources are missing.");
            }
            else if (newResource.User == null)
            {
                oldResource.RecordStatus = newStatus;
                try
                {
                    await dataService.SetScheduleAsync(oldResource);
                }
                catch (CosmosException ex)
                {
                    log.LogError(ex, "Failed to update database. Status {statusCode}", ex.Status);
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "Failed to update database.");
                }
            }
            else if (oldResource.User == null || newResource.Weeks.Count > 0)
            {
                try
                {
                    await dataService.SetScheduleAsync(newResource);
                }
                catch (CosmosException ex)
                {
                    log.LogError(ex, "Failed to update database. Status {statusCode}", ex.Status);
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "Failed to update database.");
                }
            }
        }

        [FunctionName("ScheduleRefresh_Client")]
        public async Task Start(
            [TimerTrigger("0 0 18 * * *")] TimerInfo timer,
            [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {
            List<string> users = await dataService.GetUserIdsAsync();
            await starter.StartNewAsync("ScheduleRefresh_Orchestrator", null, users);
        }

        private readonly IDataService dataService;
        private readonly IScheduleService scheduleService;
        private readonly IStoredCredentialEncryptionService encryptionService;
    }
}
