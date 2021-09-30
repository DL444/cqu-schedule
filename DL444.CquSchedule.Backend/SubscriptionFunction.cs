using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using DL444.CquSchedule.Backend.Exceptions;
using DL444.CquSchedule.Backend.Extensions;
using DL444.CquSchedule.Backend.Models;
using DL444.CquSchedule.Backend.Services;
using DL444.CquSchedule.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using User = DL444.CquSchedule.Backend.Models.User;

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
                if (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    return new OkObjectResult(calendarService.GetEmptyCalendar());
                }
                else
                {
                    log.LogError(ex, "Failed to fetch user info from database. Status: {status}", ex.StatusCode);
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
                        log.LogError(ex, "Failed to fetch term info from database. Status {status}", ex.StatusCode);
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
                log.LogError(ex, "Failed to fetch user info from database. Status: {status}", ex.StatusCode);
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
            else if (credential.Username.Length > 8)
            {
                return new BadRequestObjectResult(new DL444.CquSchedule.Models.Response<object>(localizationService.GetString("UndergraduateOnly")));
            }
            else if (!credential.Username.StartsWith("20", StringComparison.Ordinal))
            {
                return new BadRequestObjectResult(new DL444.CquSchedule.Models.Response<IcsSubscription>(localizationService.GetString("UsernameInvalid")));
            }

            string token;
            Term term;
            Schedule schedule;
            try
            {
                token = await scheduleService.SignInAsync(credential.Username, credential.Password);
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
                else if (ex.Result == AuthenticationResult.InfoRequired)
                {
                    message = localizationService.GetString("InfoRequired");
                }
                else if (ex.InnerException is SocketException)
                {
                    message = localizationService.GetString("ServerDeniedConnection");
                }
                else
                {
                    log.LogError(ex, "Unexpected response while authenticating user.");
                    return new ObjectResult(new DL444.CquSchedule.Models.Response<IcsSubscription>(localizationService.GetString("AuthErrorCannotCreate")))
                    {
                        StatusCode = 503
                    };
                }
                return new ObjectResult(new DL444.CquSchedule.Models.Response<IcsSubscription>(message))
                {
                    StatusCode = 401
                };
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to authenticate user.");
                return new ObjectResult(new DL444.CquSchedule.Models.Response<IcsSubscription>(localizationService.GetString("ServiceErrorCannotCreate")))
                {
                    StatusCode = 503
                };
            }

            try
            {
                term = await termService.GetTermAsync(async ts =>
                {
                    Term term = await scheduleService.GetTermAsync(token, TimeSpan.FromHours(8));
                    await ts.SetTermAsync(term);
                    return term;
                });
            }
            catch (CosmosException ex)
            {
                log.LogError(ex, "Failed to fetch term info from database. Status {status}", ex.StatusCode);
                return new ObjectResult(new DL444.CquSchedule.Models.Response<IcsSubscription>(localizationService.GetString("ServiceErrorCannotCreate")))
                {
                    StatusCode = 503
                };
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to fetch term info.");
                return new ObjectResult(new DL444.CquSchedule.Models.Response<IcsSubscription>(localizationService.GetString("ServiceErrorCannotCreate")))
                {
                    StatusCode = 503
                };
            }

            try
            {
                schedule = await scheduleService.GetScheduleAsync(credential.Username, term.SessionTermId, token);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to fetch schedule info.");
                return new ObjectResult(new DL444.CquSchedule.Models.Response<IcsSubscription>(localizationService.GetString("ServiceErrorCannotCreate")))
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
                return new OkObjectResult(new DL444.CquSchedule.Models.Response<IcsSubscription>(true, new IcsSubscription(null, ics), successMessage));
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
                return new OkObjectResult(new DL444.CquSchedule.Models.Response<IcsSubscription>(true, new IcsSubscription(user.SubscriptionId, null), successMessage));
            }
            catch (CosmosException ex)
            {
                log.LogError(ex, "Failed to update database. Status: {status}", ex.StatusCode);
                return new ObjectResult(new DL444.CquSchedule.Models.Response<IcsSubscription>(localizationService.GetString("ServiceErrorCannotCreate")))
                {
                    StatusCode = 503
                };
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to update database.");
                return new ObjectResult(new DL444.CquSchedule.Models.Response<IcsSubscription>(localizationService.GetString("ServiceErrorCannotCreate")))
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
                return new BadRequestObjectResult(new DL444.CquSchedule.Models.Response<object>(localizationService.GetString("UsernameInvalid")));
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
                else if (ex.Result == AuthenticationResult.InfoRequired)
                {
                    message = localizationService.GetString("InfoRequired");
                }
                else if (ex.InnerException is SocketException)
                {
                    message = localizationService.GetString("ServerDeniedConnection");
                }
                else
                {
                    log.LogError(ex, "Unexpected response while authenticating user.");
                    return new ObjectResult(new DL444.CquSchedule.Models.Response<object>(localizationService.GetString("AuthErrorCannotDelete")))
                    {
                        StatusCode = 503
                    };
                }
                return new ObjectResult(new DL444.CquSchedule.Models.Response<object>(message))
                {
                    StatusCode = 401
                };
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to authenticate user.");
                return new ObjectResult(new DL444.CquSchedule.Models.Response<object>(localizationService.GetString("ServiceErrorCannotDelete")))
                {
                    StatusCode = 503
                };
            }

            bool success = await dataService.DeleteUserAsync(credential.Username);
            if (success)
            {
                return new OkObjectResult(new DL444.CquSchedule.Models.Response<object>(true, null, localizationService.GetString("UserDeleteSuccess")));
            }
            else
            {
                log.LogError("Failed to delete user. Username: {username}", credential.Username);
                return new ObjectResult(new DL444.CquSchedule.Models.Response<object>(localizationService.GetString("ServiceErrorCannotDelete")))
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
