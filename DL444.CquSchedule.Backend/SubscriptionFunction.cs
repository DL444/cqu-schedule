using System;
using System.Threading.Tasks;
using Azure.Cosmos;
using DL444.CquSchedule.Backend.Exceptions;
using DL444.CquSchedule.Backend.Extensions;
using DL444.CquSchedule.Backend.Models;
using DL444.CquSchedule.Backend.Services;
using DL444.CquSchedule.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace DL444.CquSchedule.Backend
{
    internal class SubscriptionGetFunction
    {
        public SubscriptionGetFunction(IDataService dataService, ITermService termService, ICalendarService calendarService)
        {
            this.dataService = dataService;
            this.termService = termService;
            this.calendarService = calendarService;
        }

        [FunctionName("Subscription_Get")]
        public async Task<IActionResult> RunGetAsync(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "subscription/{username}/{subscriptionId}")] HttpRequest req,
            string username,
            string subscriptionId,
            ILogger log)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(subscriptionId))
            {
                return new BadRequestResult();
            }

            Task<Term> termTask = termService.GetTermAsync();
            Task<User> userTask = dataService.GetUserAsync(username);
            Task<Schedule> scheduleTask = dataService.GetScheduleAsync(username);
            try
            {
                User user = await userTask;
                if (!user.SubscriptionId.Equals(subscriptionId, StringComparison.Ordinal))
                {
                    return new OkObjectResult(calendarService.GetEmptyCalendar());
                }
            }
            catch (CosmosException ex)
            {
                if (ex.Status == 404)
                {
                    return new OkObjectResult(calendarService.GetEmptyCalendar());
                }
                else
                {
                    log.LogError(ex, "Failed to fetch user info from database. Status: {status}", ex.Status);
                    return new StatusCodeResult(503);
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to fetch user info from database.");
                return new StatusCodeResult(503);
            }

            try
            {
                Schedule schedule = await scheduleTask;
                if (schedule.RecordStatus == RecordStatus.StaleAuthError)
                {
                    return new OkObjectResult(calendarService.GetEmptyCalendar());
                }
                else
                {
                    try
                    {
                        Term term = await termTask;
                        return new OkObjectResult(calendarService.GetCalendar(term, schedule));
                    }
                    catch (CosmosException ex)
                    {
                        log.LogError(ex, "Failed to fetch term info from database. Status {status}", ex.Status);
                        return new StatusCodeResult(503);
                    }
                    catch (Exception ex)
                    {
                        log.LogError(ex, "Failed to fetch term info.");
                        return new StatusCodeResult(503);
                    }
                }
            }
            catch (CosmosException ex)
            {
                log.LogError(ex, "Failed to fetch user info from database. Status: {status}", ex.Status);
                return new StatusCodeResult(503);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to fetch user info from database.");
                return new StatusCodeResult(503);
            }
        }

        private readonly IDataService dataService;
        private readonly ITermService termService;
        private readonly ICalendarService calendarService;
    }

    internal class SubscriptionPostFunction
    {
        public SubscriptionPostFunction(
            IDataService dataService,
            ITermService termService,
            IScheduleService scheduleService,
            IStoredCredentialEncryptionService storedEncryptionService,
            ICalendarService calendarService,
            ILocalizationService localizationService)
        {
            this.dataService = dataService;
            this.termService = termService;
            this.scheduleService = scheduleService;
            this.storedEncryptionService = storedEncryptionService;
            this.calendarService = calendarService;
            this.localizationService = localizationService;
        }

        [FunctionName("Subscription_Post")]
        public async Task<IActionResult> RunPostAsync(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "subscription")] HttpRequest req,
            ILogger log)
        {
            Credential credential = await req.GetCredentialAsync();
            if (credential == null)
            {
                return new BadRequestResult();
            }
            else if (!credential.Username.StartsWith("20", StringComparison.Ordinal))
            {
                return new BadRequestObjectResult(new Response<IcsSubscription>(localizationService.GetString("UsernameInvalid")));
            }

            Schedule schedule;
            Term term;
            try
            {
                string token = await scheduleService.SignInAsync(credential.Username, credential.Password);
                Task<Term> termTask = termService.GetTermAsync(async ts =>
                {
                    Term term = await scheduleService.GetTermAsync(token, TimeSpan.FromHours(8));
                    await ts.SetTermAsync(term);
                    return term;
                });
                schedule = await scheduleService.GetScheduleAsync(credential.Username, token);
                try
                {
                    term = await termTask;
                }
                catch (CosmosException ex)
                {
                    log.LogError(ex, "Failed to fetch term info from database. Status {status}", ex.Status);
                    return new ObjectResult(new Response<IcsSubscription>(localizationService.GetString("ServiceErrorCannotCreate")))
                    {
                        StatusCode = 503
                    };
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "Failed to fetch term info.");
                    return new ObjectResult(new Response<IcsSubscription>(localizationService.GetString("ServiceErrorCannotCreate")))
                    {
                        StatusCode = 503
                    };
                }
            }
            catch (AuthenticationException ex)
            {
                string message;
                if (ex.Result == AuthenticationResult.IncorrectCredential)
                {
                    message = localizationService.GetString("CredentialError");
                }
                else if (ex.Result == AuthenticationResult.CaptchaRequired)
                {
                    message = localizationService.GetString("CaptchaRequired");
                }
                else
                {
                    log.LogError(ex, "Unexpected response while authenticating user.");
                    return new ObjectResult(new Response<IcsSubscription>(localizationService.GetString("ServiceErrorCannotCreate")))
                    {
                        StatusCode = 503
                    };
                }
                return new ObjectResult(new Response<IcsSubscription>(message))
                {
                    StatusCode = 401
                };
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to authenticate user.");
                return new ObjectResult(new Response<IcsSubscription>(localizationService.GetString("ServiceErrorCannotCreate")))
                {
                    StatusCode = 503
                };
            }

            string ics = calendarService.GetCalendar(term, schedule);
            int vacationServeDays = calendarService.VacationCalendarServeDays;
            string successMessage = (DateTimeOffset.Now > term.EndDate.AddDays(vacationServeDays) || DateTimeOffset.Now < term.StartDate.AddDays(-vacationServeDays))
                ? localizationService.GetString("OnVacationCalendarMayBeEmpty", localizationService.DefaultCulture, vacationServeDays)
                : null;

            if (!credential.ShouldSaveCredential)
            {
                return new OkObjectResult(new Response<IcsSubscription>(true, new IcsSubscription(null, ics), successMessage));
            }

            User user = new User()
            {
                Username = credential.Username,
                Password = credential.Password,
                SubscriptionId = Guid.NewGuid().ToString()
            };
            user = await storedEncryptionService.EncryptAsync(user);

            try
            {
                await dataService.SetUserAsync(user);
                await dataService.SetScheduleAsync(schedule);
                return new OkObjectResult(new Response<IcsSubscription>(true, new IcsSubscription(user.SubscriptionId, null), successMessage));
            }
            catch (CosmosException ex)
            {
                log.LogError(ex, "Failed to update database. Status: {status}", ex.Status);
                return new ObjectResult(new Response<IcsSubscription>(localizationService.GetString("ServiceErrorCannotCreate")))
                {
                    StatusCode = 503
                };
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to update database.");
                return new ObjectResult(new Response<IcsSubscription>(localizationService.GetString("ServiceErrorCannotCreate")))
                {
                    StatusCode = 503
                };
            }
        }

        private readonly IDataService dataService;
        private readonly ITermService termService;
        private readonly IScheduleService scheduleService;
        private readonly IStoredCredentialEncryptionService storedEncryptionService;
        private readonly ICalendarService calendarService;
        private readonly ILocalizationService localizationService;
    }

    internal class SubscriptionDeleteFunction
    {
        public SubscriptionDeleteFunction(IDataService dataService, IScheduleService scheduleService, ILocalizationService localizationService)
        {
            this.dataService = dataService;
            this.scheduleService = scheduleService;
            this.localizationService = localizationService;
        }

        [FunctionName("Subscription_Delete")]
        public async Task<IActionResult> RunDeleteAsync(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "subscription/delete")] HttpRequest req,
            ILogger log)
        {
            Credential credential = await req.GetCredentialAsync();
            if (credential == null)
            {
                return new BadRequestResult();
            }
            else if (!credential.Username.StartsWith("20", StringComparison.Ordinal))
            {
                return new BadRequestObjectResult(new Response<object>(localizationService.GetString("UsernameInvalid")));
            }

            try
            {
                await scheduleService.SignInAsync(credential.Username, credential.Password);
            }
            catch (AuthenticationException ex)
            {
                string message;
                if (ex.Result == AuthenticationResult.IncorrectCredential)
                {
                    message = localizationService.GetString("CredentialError");
                }
                else if (ex.Result == AuthenticationResult.CaptchaRequired)
                {
                    message = localizationService.GetString("CaptchaRequired");
                }
                else
                {
                    log.LogError(ex, "Unexpected response while authenticating user.");
                    return new ObjectResult(new Response<object>(localizationService.GetString("ServiceErrorCannotCreate")))
                    {
                        StatusCode = 503
                    };
                }
                return new ObjectResult(new Response<object>(message))
                {
                    StatusCode = 401
                };
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to authenticate user.");
                return new ObjectResult(new Response<object>(localizationService.GetString("ServiceErrorCannotDelete")))
                {
                    StatusCode = 503
                };
            }

            bool success = await dataService.DeleteUserAsync(credential.Username);
            if (success)
            {
                return new OkObjectResult(new Response<object>(true, null, localizationService.GetString("UserDeleteSuccess")));
            }
            else
            {
                log.LogError("Failed to delete user. Username: {username}", credential.Username);
                return new ObjectResult(new Response<object>(localizationService.GetString("ServiceErrorCannotDelete")))
                {
                    StatusCode = 503
                };
            }
        }

        private readonly IDataService dataService;
        private readonly IScheduleService scheduleService;
        private readonly ILocalizationService localizationService;
    }
}
