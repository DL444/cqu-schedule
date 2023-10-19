using System;
using System.Net;
using System.Threading.Tasks;
using DL444.CquSchedule.Backend.Extensions;
using DL444.CquSchedule.Backend.Models;
using DL444.CquSchedule.Backend.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DL444.CquSchedule.Backend
{
    internal sealed class WarmupFunction
    {
        public WarmupFunction(
            IConfiguration config,
            IDataService dataService,
            UndergraduateScheduleService undergradScheduleService,
            ITermService termService,
            IStoredCredentialEncryptionService storedEncryptionService)
        {
            warmupUser = config.GetValue<string>("Warmup:User");
            this.dataService = dataService;
            this.scheduleService = undergradScheduleService;
            this.termService = termService;
            this.storedEncryptionService = storedEncryptionService;
        }

        [Function("Warmup")]
        public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequestData req)
        {
            ILogger log = req.FunctionContext.GetFunctionNamedLogger();
            if (warmupExecuted)
            {
                return req.CreateResponse(HttpStatusCode.OK);
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
                    if (user.UserType == UserType.Undergraduate)
                    {
                        user = await storedEncryptionService.DecryptAsync(user);
                        ISignInContext signInContext = await scheduleService.SignInAsync(user.Username, user.Password);
                        Term term = await termService.GetTermAsync(async ts =>
                        {
                            if (!scheduleService.SupportsMultiterm)
                            {
                                return default;
                            }
                            Term term = await scheduleService.GetTermAsync(signInContext, TimeSpan.FromHours(8));
                            await ts.SetTermAsync(term);
                            return term;
                        });
                        await scheduleService.GetScheduleAsync(user.Username, term.SessionTermId, signInContext, TimeSpan.FromHours(8));
                    }
                    else
                    {
                        log.LogWarning("Warmup user is set to a postgraduate student, which is not supported.");
                    }
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

            return req.CreateResponse(HttpStatusCode.OK);
        }

        private static bool warmupExecuted;
        private readonly string warmupUser;
        private readonly IDataService dataService;
        private readonly IScheduleService scheduleService;
        private readonly ITermService termService;
        private readonly IStoredCredentialEncryptionService storedEncryptionService;
    }
}
