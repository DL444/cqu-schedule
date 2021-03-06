using System;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Cosmos;
using DL444.CquSchedule.Backend.Exceptions;
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
    internal class SubscriptionFunction
    {
        public SubscriptionFunction(
            IDataService dataService,
            IScheduleService scheduleService,
            IStoredCredentialEncryptionService storedEncryptionService,
            ICalendarService calendarService,
            ILocalizationService localizationService)
        {
            this.dataService = dataService;
            this.scheduleService = scheduleService;
            this.storedEncryptionService = storedEncryptionService;
            this.calendarService = calendarService;
            this.localizationService = localizationService;
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
                    return new OkObjectResult(calendarService.GetCalendar(schedule));
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

        [FunctionName("Subscription_Post")]
        public async Task<IActionResult> RunPostAsync(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "subscription")] HttpRequest req,
            ILogger log)
        {
            Credential credential = await GetCredentialAsync(req);
            if (credential == null)
            {
                return new BadRequestResult();
            }
            else if (!credential.Username.StartsWith("20", StringComparison.Ordinal))
            {
                return new BadRequestObjectResult(new Response<IcsSubscription>(localizationService.GetString("UsernameInvalid")));
            }

            Schedule schedule;
            try
            {
                string token = await scheduleService.SignInAsync(credential.Username, credential.Password);
                schedule = await scheduleService.GetScheduleAsync(credential.Username, token);
            }
            catch (AuthenticationException ex)
            {
                string message;
                if (ex.Reason == AuthenticationFailedReason.IncorrectCredential)
                {
                    message = localizationService.GetString("CredentialError");
                }
                else if (ex.Reason == AuthenticationFailedReason.CaptchaRequired)
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
            string ics = calendarService.GetCalendar(schedule);

            if (!credential.ShouldSaveCredential)
            {
                return new OkObjectResult(new Response<IcsSubscription>(new IcsSubscription(null, ics)));
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
                return new OkObjectResult(new Response<IcsSubscription>(new IcsSubscription(user.SubscriptionId, null)));
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

        [FunctionName("Subscription_Delete")]
        public async Task<IActionResult> RunDeleteAsync(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "subscription/delete")] HttpRequest req,
            ILogger log)
        {
            Credential credential = await GetCredentialAsync(req);
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
                if (ex.Reason == AuthenticationFailedReason.IncorrectCredential)
                {
                    message = localizationService.GetString("CredentialError");
                }
                else if (ex.Reason == AuthenticationFailedReason.CaptchaRequired)
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

        private async Task<Credential> GetCredentialAsync(HttpRequest request)
        {
            try
            {
                Credential credential = await JsonSerializer.DeserializeAsync<Credential>(request.Body, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true });
                if (credential == null || string.IsNullOrWhiteSpace(credential.Username) || string.IsNullOrWhiteSpace(credential.Password))
                {
                    return default;
                }
                else
                {
                    return credential;
                }
            }
            catch (JsonException)
            {
                return default;
            }
        }

        private readonly IDataService dataService;
        private readonly IScheduleService scheduleService;
        private readonly IStoredCredentialEncryptionService storedEncryptionService;
        private readonly ICalendarService calendarService;
        private readonly ILocalizationService localizationService;
    }
}
