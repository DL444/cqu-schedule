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
    internal sealed class SubscriptionGetFunction
    {
        public SubscriptionGetFunction(IDataService dataService, ITermService termService, ICalendarService calendarService)
        {
            this.dataService = dataService;
            this.termService = termService;
            this.calendarService = calendarService;
        }

        [FunctionName("Subscription_Get")]
        public Task<IActionResult> RunGetAsync(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "subscription/{username}/{subscriptionId}")] HttpRequest req,
            string username,
            string subscriptionId,
            ILogger log)
            => GetScheduleAsync(username, subscriptionId, CalenderEventCategories.All, log);

        [FunctionName("Course_Get")]
        public Task<IActionResult> RunGetCoursesAsync(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "course/{username}/{subscriptionId}")] HttpRequest req,
            string username,
            string subscriptionId,
            ILogger log)
            => GetScheduleAsync(username, subscriptionId, CalenderEventCategories.Courses, log);

        [FunctionName("Exam_Get")]
        public Task<IActionResult> RunGetExamsAsync(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "exam/{username}/{subscriptionId}")] HttpRequest req,
            string username,
            string subscriptionId,
            ILogger log)
            => GetScheduleAsync(username, subscriptionId, CalenderEventCategories.Exams, log);

        private async Task<IActionResult> GetScheduleAsync(string username, string subscriptionId, CalenderEventCategories eventCategories, ILogger log)
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
                        return new ContentResult()
                        {
                            Content = calendarService.GetCalendar(term, schedule, eventCategories),
                            ContentType = "text/calendar; charset=utf-8",
                            StatusCode = 200
                        };
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

    internal sealed class SubscriptionPostFunction
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
                var response = new CquSchedule.Models.Response<IcsSubscription>(localizationService.GetString("UndergraduateOnly"));
                return IcsSubscriptionResponseSerializerContext.Default.GetSerializedResponse(response, 400);
            }
            else if (!credential.Username.StartsWith("20", StringComparison.Ordinal))
            {
                var response = new CquSchedule.Models.Response<IcsSubscription>(localizationService.GetString("UsernameInvalid"));
                return IcsSubscriptionResponseSerializerContext.Default.GetSerializedResponse(response, 400);
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
                var statusCode = 401;
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
                    message = localizationService.GetString("AuthErrorCannotCreate");
                    statusCode = 503;
                }
                var response = new CquSchedule.Models.Response<IcsSubscription>(message);
                return IcsSubscriptionResponseSerializerContext.Default.GetSerializedResponse(response, statusCode);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to authenticate user.");
                var response = new CquSchedule.Models.Response<IcsSubscription>(localizationService.GetString("ServiceErrorCannotCreate"));
                return IcsSubscriptionResponseSerializerContext.Default.GetSerializedResponse(response, 503);
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
                var response = new CquSchedule.Models.Response<IcsSubscription>(localizationService.GetString("ServiceErrorCannotCreate"));
                return IcsSubscriptionResponseSerializerContext.Default.GetSerializedResponse(response, 503);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to fetch term info.");
                var response = new CquSchedule.Models.Response<IcsSubscription>(localizationService.GetString("ServiceErrorCannotCreate"));
                return IcsSubscriptionResponseSerializerContext.Default.GetSerializedResponse(response, 503);
            }

            try
            {
                schedule = await scheduleService.GetScheduleAsync(credential.Username, term.SessionTermId, token, TimeSpan.FromHours(8));
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to fetch schedule info.");
                var response = new CquSchedule.Models.Response<IcsSubscription>(localizationService.GetString("ServiceErrorCannotCreate"));
                return IcsSubscriptionResponseSerializerContext.Default.GetSerializedResponse(response, 503);
            }

            int vacationServeDays = calendarService.VacationCalendarServeDays;
            string successMessage = (DateTimeOffset.Now > term.EndDate.AddDays(vacationServeDays) || DateTimeOffset.Now < term.StartDate.AddDays(-vacationServeDays))
                ? localizationService.GetString("OnVacationCalendarMayBeEmpty", localizationService.DefaultCulture, vacationServeDays)
                : null;

            if (!credential.ShouldSaveCredential)
            {
                string ics = calendarService.GetCalendar(term, schedule);
                var response = new CquSchedule.Models.Response<IcsSubscription>(true, new IcsSubscription(null, ics), successMessage);
                return IcsSubscriptionResponseSerializerContext.Default.GetSerializedResponse(response);
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
                var response = new CquSchedule.Models.Response<IcsSubscription>(true, new IcsSubscription(user.SubscriptionId, null), successMessage);
                return IcsSubscriptionResponseSerializerContext.Default.GetSerializedResponse(response);
            }
            catch (CosmosException ex)
            {
                log.LogError(ex, "Failed to update database. Status: {status}", ex.StatusCode);
                var response = new CquSchedule.Models.Response<IcsSubscription>(localizationService.GetString("ServiceErrorCannotCreate"));
                return IcsSubscriptionResponseSerializerContext.Default.GetSerializedResponse(response, 503);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to update database.");
                var response = new CquSchedule.Models.Response<IcsSubscription>(localizationService.GetString("ServiceErrorCannotCreate"));
                return IcsSubscriptionResponseSerializerContext.Default.GetSerializedResponse(response, 503);
            }
        }

        private readonly IDataService dataService;
        private readonly ITermService termService;
        private readonly IScheduleService scheduleService;
        private readonly IStoredCredentialEncryptionService storedEncryptionService;
        private readonly ICalendarService calendarService;
        private readonly ILocalizationService localizationService;
    }

    internal sealed class SubscriptionDeleteFunction
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
                var response = new CquSchedule.Models.Response<int>(localizationService.GetString("UsernameInvalid"));
                return StatusOnlyResponseSerializerContext.Default.GetSerializedResponse(response, 400);
            }

            try
            {
                await scheduleService.SignInAsync(credential.Username, credential.Password);
            }
            catch (AuthenticationException ex)
            {
                string message;
                var statusCode = 401;
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
                    message = localizationService.GetString("AuthErrorCannotDelete");
                    statusCode = 503;
                }
                var response = new DL444.CquSchedule.Models.Response<int>(message);
                return StatusOnlyResponseSerializerContext.Default.GetSerializedResponse(response, statusCode);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to authenticate user.");
                var response = new DL444.CquSchedule.Models.Response<int>(localizationService.GetString("ServiceErrorCannotDelete"));
                return StatusOnlyResponseSerializerContext.Default.GetSerializedResponse(response, 503);
            }

            bool success = await dataService.DeleteUserAsync(credential.Username);
            if (success)
            {
                var response = new DL444.CquSchedule.Models.Response<int>(true, default, localizationService.GetString("UserDeleteSuccess"));
                return StatusOnlyResponseSerializerContext.Default.GetSerializedResponse(response);
            }
            else
            {
                log.LogError("Failed to delete user. Username: {username}", credential.Username);
                var response = new DL444.CquSchedule.Models.Response<int>(localizationService.GetString("ServiceErrorCannotDelete"));
                return StatusOnlyResponseSerializerContext.Default.GetSerializedResponse(response, 503);
            }
        }

        private readonly IDataService dataService;
        private readonly IScheduleService scheduleService;
        private readonly ILocalizationService localizationService;
    }
}
