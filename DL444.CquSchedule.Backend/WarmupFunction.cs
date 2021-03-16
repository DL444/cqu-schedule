using System;
using System.Threading.Tasks;
using DL444.CquSchedule.Backend.Models;
using DL444.CquSchedule.Backend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DL444.CquSchedule.Backend
{
    internal class WarmupFunction
    {
        public WarmupFunction(
            IConfiguration config,
            IDataService dataService,
            IScheduleService scheduleService,
            IStoredCredentialEncryptionService storedEncryptionService) 
        {
            warmupUser = config.GetValue<string>("Warmup:User");
            this.dataService = dataService;
            this.scheduleService = scheduleService;
            this.storedEncryptionService = storedEncryptionService;
        }

        [FunctionName("Warmup")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequest req,
            ILogger log)
        {
            if (warmupExecuted)
            {
                return new OkResult();
            }

            if (!string.IsNullOrEmpty(warmupUser))
            {
                // Perform a user and schedule fetch once and discard the result.
                // After this, the code is jitted, database metadata is cached, and established network connections might be kept.
                // Therefore, further requests initiated by users might become more responsive.
                warmupExecuted = true;
                try
                {
                    User user = await dataService.GetUserAsync(warmupUser);
                    user = await storedEncryptionService.DecryptAsync(user);
                    string token = await scheduleService.SignInAsync(user.Username, user.Password);
                    await scheduleService.GetScheduleAsync(user.Username, token);
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "Failed to execute warmup requests. This is expected for a new deployment since no user exists.");
                }
            }
            else
            {
                log.LogInformation("Warmup user is not set. Setting one can probably improve responsiveness of later requests.");
            }

            return new OkResult();
        }

        private static bool warmupExecuted;
        private readonly string warmupUser;
        private readonly IDataService dataService;
        private readonly IScheduleService scheduleService;
        private readonly IStoredCredentialEncryptionService storedEncryptionService;
    }
}
