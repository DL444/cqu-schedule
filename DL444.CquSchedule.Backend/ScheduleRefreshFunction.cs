using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using DL444.CquSchedule.Backend.Exceptions;
using DL444.CquSchedule.Backend.Models;
using DL444.CquSchedule.Backend.Services;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using User = DL444.CquSchedule.Backend.Models.User;

namespace DL444.CquSchedule.Backend
{
    internal class ScheduleRefreshFunction
    {
        public ScheduleRefreshFunction(
            IDataService dataService,
            ITermService termService,
            IScheduleService scheduleService,
            IStoredCredentialEncryptionService encryptionService)
        {
            this.dataService = dataService;
            this.termService = termService;
            this.scheduleService = scheduleService;
            this.encryptionService = encryptionService;
        }

        [FunctionName("ScheduleRefresh_Orchestrator")]
        public async Task RunOrchestratorAsync(
            [OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            string sessionTermId = default;
            var users = context.GetInput<List<string>>();
            foreach (var user in users)
            {
                ScheduleRefreshInput input = new ScheduleRefreshInput()
                {
                    Username = user,
                    SessionTermId = sessionTermId
                };
                sessionTermId = await context.CallActivityAsync<string>("ScheduleRefresh_Activity", input);
            }
        }

        [FunctionName("ScheduleRefresh_Activity")]
        public async Task<string> RefreshAsync([ActivityTrigger] ScheduleRefreshInput input, ILogger log)
        {
            (bool getUserSuccess, User user) = await GetUserAsync(input.Username, log);
            if (!getUserSuccess)
            {
                return input.SessionTermId;
            }
            Task<(bool, Schedule)> oldFetchTask = GetOldScheduleAsync(user.Username, log);
            (AuthenticationResult authResult, string token) = await SignInAsync(user, log);

            bool newFetchSuccess;
            Schedule newSchedule;
            RecordStatus newStatus;
            if (authResult == AuthenticationResult.Success)
            {
                Task<bool> termUpdateTask = null;
                (bool getTermSuccess, Term newTerm) = await GetTermAsync(input.TermRefreshed, token, log);
                if (getTermSuccess)
                {
                    termUpdateTask = input.TermRefreshed ? null : UpdateTermAsync(newTerm, log);
                    (newFetchSuccess, newSchedule) = await GetNewScheduleAsync(user.Username, newTerm.SessionTermId, token, log);
                    newStatus = newFetchSuccess ? RecordStatus.UpToDate : RecordStatus.StaleUpstreamError;
                }
                else
                {
                    newFetchSuccess = false;
                    newSchedule = default;
                    newStatus = RecordStatus.StaleUpstreamError;
                }

                if (termUpdateTask != null && (await termUpdateTask) == true)
                {
                    input.SessionTermId = newTerm.SessionTermId;
                }
            }
            else
            {
                newFetchSuccess = false;
                newSchedule = default;
                newStatus = authResult == AuthenticationResult.IncorrectCredential ? RecordStatus.StaleAuthError : RecordStatus.StaleUpstreamError;
            }

            (bool oldFetchSuccess, Schedule oldSchedule) = await oldFetchTask;
            if (newFetchSuccess)
            {
                if (!oldFetchSuccess || oldSchedule.RecordStatus != RecordStatus.UpToDate || newSchedule != oldSchedule)
                {
                    await UpdateScheduleAsync(newSchedule, log);
                }
            }
            else
            {
                if (oldFetchSuccess && oldSchedule.User != null)
                {
                    oldSchedule.RecordStatus = newStatus;
                    await UpdateScheduleAsync(oldSchedule, log);
                }
            }
            return input.SessionTermId;
        }

        [FunctionName("ScheduleRefresh_Client")]
        public async Task StartAsync(
            [TimerTrigger("0 0 8 * * *")] TimerInfo timer,
            [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {
            List<string> users = await dataService.GetUserIdsAsync();
            log.LogInformation("Current active user count: {userCount}.", users.Count);
            await starter.StartNewAsync("ScheduleRefresh_Orchestrator", null, users);
        }

        private async Task<(bool success, User user)> GetUserAsync(string username, ILogger log)
        {
            try
            {
                User user = await dataService.GetUserAsync(username);
                user = await encryptionService.DecryptAsync(user);
                return (true, user);
            }
            catch (CosmosException ex)
            {
                log.LogError(ex, "Failed to fetch user credential from database. Status: {status}", ex.StatusCode);
                return (false, default);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to fetch user credential from database.");
                return (false, default);
            }
        }

        private async Task<(AuthenticationResult result, string token)> SignInAsync(User user, ILogger log)
        {
            try
            {
                string token = await scheduleService.SignInAsync(user.Username, user.Password);
                return (AuthenticationResult.Success, token);
            }
            catch (AuthenticationException ex)
            {
                if (ex.InnerException is SocketException)
                {
                    log.LogError(ex, "User authentication failed. Server closed connection.");
                }
                else if (ex.Result != AuthenticationResult.IncorrectCredential && ex.Result != AuthenticationResult.InfoRequired)
                {
                    log.LogError(ex, "User authentication failed. Probably captcha required.");
                }
                return (ex.Result, default);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "User authentication failed.");
                return (AuthenticationResult.UnknownFailure, default);
            }
        }

        private async Task<(bool success, Term term)> GetTermAsync(bool termRefreshed, string token, ILogger log)
        {
            try
            {
                Term newTerm = termRefreshed ? await termService.GetTermAsync() : await scheduleService.GetTermAsync(token, TimeSpan.FromHours(8));
                return (true, newTerm);
            }
            catch (CosmosException ex)
            {
                log.LogError(ex, "Failed to fetch term from database. Status {status}", ex.StatusCode);
                return (false, default);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to fetch term from server.");
                return (false, default);
            }
        }

        private async Task<bool> UpdateTermAsync(Term term, ILogger log)
        {
            try
            {
                await termService.SetTermAsync(term);
                return true;
            }
            catch (CosmosException ex)
            {
                log.LogError(ex, "Failed to update term in database. Status {status}", ex.StatusCode);
                return false;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to update term in database.");
                return false;
            }
        }

        private async Task<(bool success, Schedule newSchedule)> GetNewScheduleAsync(string username, string termId, string token, ILogger log)
        {
            try
            {
                Schedule newSchedule = await scheduleService.GetScheduleAsync(username, termId, token, TimeSpan.FromHours(8));
                return (true, newSchedule);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to fetch schedule from upstream.");
                return (false, default);
            }
        }

        private async Task<(bool success, Schedule oldSchedule)> GetOldScheduleAsync(string username, ILogger log)
        {
            try
            {
                Schedule oldSchedule = await dataService.GetScheduleAsync(username);
                return (true, oldSchedule);
            }
            catch (CosmosException ex)
            {
                if (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    return (true, default);
                }
                else
                {
                    log.LogError(ex, "Failed to fetch resource from database. Status: {status}", ex.StatusCode);
                    return (false, default);
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to fetch resource from database.");
                return (false, default);
            }
        }

        private async Task<bool> UpdateScheduleAsync(Schedule schedule, ILogger log)
        {
            try
            {
                await dataService.SetScheduleAsync(schedule);
                return true;
            }
            catch (CosmosException ex)
            {
                log.LogError(ex, "Failed to update database. Status {statusCode}", ex.StatusCode);
                return false;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to update database.");
                return false;
            }
        }

        private readonly IDataService dataService;
        private readonly ITermService termService;
        private readonly IScheduleService scheduleService;
        private readonly IStoredCredentialEncryptionService encryptionService;
    }
}
