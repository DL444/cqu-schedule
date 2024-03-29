using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using DL444.CquSchedule.Backend.Exceptions;
using DL444.CquSchedule.Backend.Extensions;
using DL444.CquSchedule.Backend.Models;
using DL444.CquSchedule.Backend.Services;
using DL444.CquSchedule.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
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

        [Function("Subscription_Get")]
        public Task<HttpResponseData> RunGetAsync(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "subscription/{username}/{subscriptionId}")] HttpRequestData req,
            string username,
            string subscriptionId)
            => GetScheduleAsync(req, username, subscriptionId, CalenderEventCategories.All);

        [Function("Course_Get")]
        public Task<HttpResponseData> RunGetCoursesAsync(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "course/{username}/{subscriptionId}")] HttpRequestData req,
            string username,
            string subscriptionId)
            => GetScheduleAsync(req, username, subscriptionId, CalenderEventCategories.Courses);

        [Function("Exam_Get")]
        public Task<HttpResponseData> RunGetExamsAsync(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "exam/{username}/{subscriptionId}")] HttpRequestData req,
            string username,
            string subscriptionId)
            => GetScheduleAsync(req, username, subscriptionId, CalenderEventCategories.Exams);

        private async Task<HttpResponseData> GetScheduleAsync(HttpRequestData req, string username, string subscriptionId, CalenderEventCategories eventCategories)
        {
            ILogger log = req.FunctionContext.GetFunctionNamedLogger();
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(subscriptionId))
            {
                return req.CreateResponse(HttpStatusCode.BadRequest);
            }

            Task<Term> termTask = termService.GetTermAsync();
            Task<User> userTask = dataService.GetUserAsync(username);
            Task<Schedule> scheduleTask = dataService.GetScheduleAsync(username);
            try
            {
                User user = await userTask;
                if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(user.SubscriptionId), Encoding.UTF8.GetBytes(subscriptionId)))
                {
                    return req.CreateStringContentResponse(HttpStatusCode.OK, calendarService.GetEmptyCalendar(), calendarContentType);
                }
            }
            catch (CosmosException ex)
            {
                if (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    return req.CreateStringContentResponse(HttpStatusCode.OK, calendarService.GetEmptyCalendar(), calendarContentType);
                }
                else
                {
                    log.LogError(ex, "Failed to fetch user info from database. Status: {status}", ex.StatusCode);
                    return req.CreateResponse(HttpStatusCode.ServiceUnavailable);
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to fetch user info from database.");
                return req.CreateResponse(HttpStatusCode.ServiceUnavailable);
            }

            try
            {
                Schedule schedule = await scheduleTask;
                if (schedule.RecordStatus == RecordStatus.StaleAuthError)
                {
                    return req.CreateStringContentResponse(HttpStatusCode.OK, calendarService.GetEmptyCalendar(), calendarContentType);
                }
                else
                {
                    try
                    {
                        Term term = await termTask;
                        return req.CreateStringContentResponse(HttpStatusCode.OK, calendarService.GetCalendar(username, term, schedule, eventCategories), calendarContentType);
                    }
                    catch (CosmosException ex)
                    {
                        log.LogError(ex, "Failed to fetch term info from database. Status {status}", ex.StatusCode);
                        return req.CreateResponse(HttpStatusCode.ServiceUnavailable);
                    }
                    catch (Exception ex)
                    {
                        log.LogError(ex, "Failed to fetch term info.");
                        return req.CreateResponse(HttpStatusCode.ServiceUnavailable);
                    }
                }
            }
            catch (CosmosException ex)
            {
                log.LogError(ex, "Failed to fetch user info from database. Status: {status}", ex.StatusCode);
                return req.CreateResponse(HttpStatusCode.ServiceUnavailable);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to fetch user info from database.");
                return req.CreateResponse(HttpStatusCode.ServiceUnavailable);
            }
        }

        private readonly IDataService dataService;
        private readonly ITermService termService;
        private readonly ICalendarService calendarService;
        private readonly string calendarContentType = "text/calendar; charset=utf-8";
    }

    internal sealed class SubscriptionPostFunction
    {
        public SubscriptionPostFunction(
            IDataService dataService,
            ITermService termService,
            UndergraduateScheduleService undergradScheduleService,
            PostgraduateScheduleService postgradScheduleService,
            IStoredCredentialEncryptionService storedEncryptionService,
            ICalendarService calendarService,
            ILocalizationService localizationService)
        {
            this.dataService = dataService;
            this.termService = termService;
            this.undergradScheduleService = undergradScheduleService;
            this.postgradScheduleService = postgradScheduleService;
            this.storedEncryptionService = storedEncryptionService;
            this.calendarService = calendarService;
            this.localizationService = localizationService;
        }

        [Function("Subscription_Post")]
        public async Task<HttpResponseData> RunPostAsync([HttpTrigger(AuthorizationLevel.Function, "post", Route = "subscription")] HttpRequestData req)
        {
            ILogger log = req.FunctionContext.GetFunctionNamedLogger();
            Credential credential = await req.GetCredentialAsync();
            if (credential == null)
            {
                return req.CreateResponse(HttpStatusCode.BadRequest);
            }
            else if (!credential.Username.StartsWith("20", StringComparison.Ordinal))
            {
                var response = new CquSchedule.Models.Response<IcsSubscription>(localizationService.GetString("UsernameInvalid"));
                return IcsSubscriptionResponseSerializerContext.Default.GetSerializedResponse(req, response, HttpStatusCode.BadRequest);
            }

            UserType userType;
            IScheduleService scheduleService;
            switch (credential.Username.Length)
            {
                case 8:
                    userType = UserType.Undergraduate;
                    scheduleService = undergradScheduleService;
                    break;
                case 12:
                case 13:
                    userType = UserType.Postgraduate;
                    scheduleService = postgradScheduleService;
                    break;
                default:
                    var response = new CquSchedule.Models.Response<IcsSubscription>(localizationService.GetString("UsernameInvalid"));
                    return IcsSubscriptionResponseSerializerContext.Default.GetSerializedResponse(req, response, HttpStatusCode.BadRequest);
            }

            ISignInContext signInContext;
            Term term;
            Schedule schedule;
            try
            {
                signInContext = await scheduleService.SignInAsync(credential.Username, credential.Password);
            }
            catch (AuthenticationException ex)
            {
                string message;
                var statusCode = HttpStatusCode.Unauthorized;
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
                else if (ex.Result == AuthenticationResult.ConnectionFailed)
                {
                    message = localizationService.GetString("ServiceErrorConnectionFailure");
                }
                else if (ex.InnerException is SocketException)
                {
                    message = localizationService.GetString("ServerDeniedConnection");
                }
                else
                {
                    log.LogError(ex, "Unexpected response while authenticating user.");
                    message = localizationService.GetString("AuthErrorCannotCreate");
                    statusCode = HttpStatusCode.ServiceUnavailable;
                }
                var response = new CquSchedule.Models.Response<IcsSubscription>(message);
                return IcsSubscriptionResponseSerializerContext.Default.GetSerializedResponse(req, response, statusCode);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to authenticate user.");
                var response = new CquSchedule.Models.Response<IcsSubscription>(localizationService.GetString("ServiceErrorCannotCreate"));
                return IcsSubscriptionResponseSerializerContext.Default.GetSerializedResponse(req, response, HttpStatusCode.ServiceUnavailable);
            }

            try
            {
                term = await termService.GetTermAsync(async ts =>
                {
                    if (!scheduleService.SupportsMultiterm)
                    {
                        log.LogError("Reading term from database failed, but current user is incapable of fetching from upstream.");
                        return default;
                    }
                    Term term = await scheduleService.GetTermAsync(signInContext, TimeSpan.FromHours(8));
                    await ts.SetTermAsync(term);
                    return term;
                });
            }
            catch (CosmosException ex)
            {
                log.LogError(ex, "Failed to fetch term info from database. Status {status}", ex.StatusCode);
                var response = new CquSchedule.Models.Response<IcsSubscription>(localizationService.GetString("ServiceErrorCannotCreate"));
                return IcsSubscriptionResponseSerializerContext.Default.GetSerializedResponse(req, response, HttpStatusCode.ServiceUnavailable);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to fetch term info.");
                var response = new CquSchedule.Models.Response<IcsSubscription>(localizationService.GetString("ServiceErrorCannotCreate"));
                return IcsSubscriptionResponseSerializerContext.Default.GetSerializedResponse(req, response, HttpStatusCode.ServiceUnavailable);
            }

            try
            {
                schedule = await scheduleService.GetScheduleAsync(credential.Username, term.SessionTermId, signInContext, TimeSpan.FromHours(8));
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to fetch schedule info.");
                var response = new CquSchedule.Models.Response<IcsSubscription>(localizationService.GetString("ServiceErrorCannotCreate"));
                return IcsSubscriptionResponseSerializerContext.Default.GetSerializedResponse(req, response, HttpStatusCode.ServiceUnavailable);
            }

            int vacationServeDays = calendarService.VacationCalendarServeDays;
            bool onVacation = scheduleService.SupportsMultiterm
                && (DateTimeOffset.Now > term.EndDate.AddDays(vacationServeDays) || DateTimeOffset.Now < term.StartDate.AddDays(-vacationServeDays));
            string successMessage = onVacation ? localizationService.GetString("OnVacationCalendarMayBeEmpty", localizationService.DefaultCulture, vacationServeDays) : null;

            if (!credential.ShouldSaveCredential)
            {
                string ics = calendarService.GetCalendar(credential.Username, term, schedule);
                var response = new CquSchedule.Models.Response<IcsSubscription>(true, new IcsSubscription(null, ics), successMessage);
                return IcsSubscriptionResponseSerializerContext.Default.GetSerializedResponse(req, response);
            }

            User user = new User()
            {
                Username = credential.Username,
                Password = credential.Password,
                SubscriptionId = CreateSubscriptionId(),
                UserType = userType
            };
            user = await storedEncryptionService.EncryptAsync(user);

            try
            {
                await dataService.SetUserAsync(user);
                await dataService.SetScheduleAsync(schedule);
                var response = new CquSchedule.Models.Response<IcsSubscription>(true, new IcsSubscription(user.SubscriptionId, null), successMessage);
                return IcsSubscriptionResponseSerializerContext.Default.GetSerializedResponse(req, response);
            }
            catch (CosmosException ex)
            {
                log.LogError(ex, "Failed to update database. Status: {status}", ex.StatusCode);
                var response = new CquSchedule.Models.Response<IcsSubscription>(localizationService.GetString("ServiceErrorCannotCreate"));
                return IcsSubscriptionResponseSerializerContext.Default.GetSerializedResponse(req, response, HttpStatusCode.ServiceUnavailable);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to update database.");
                var response = new CquSchedule.Models.Response<IcsSubscription>(localizationService.GetString("ServiceErrorCannotCreate"));
                return IcsSubscriptionResponseSerializerContext.Default.GetSerializedResponse(req, response, HttpStatusCode.ServiceUnavailable);
            }
        }

        private static string CreateSubscriptionId()
        {
            Span<byte> rngBuffer = stackalloc byte[16];
            Span<char> base64Buffer = stackalloc char[24];
            RandomNumberGenerator.Fill(rngBuffer);
            bool success = Convert.TryToBase64Chars(rngBuffer, base64Buffer, out _);
            if (!success)
            {
                throw new UnreachableException("Internal buffer overflow creating subscription ID.");
            }

            // We need a URL-safe Base64 string as specified by RFC 4648 Section 5, which replaces two characters and removes padding.
            // This is fine even though .NET currently does not natively support this flavor, since we only use the string opaquely and never decode.
            int index = base64Buffer.IndexOfAny('+', '/', '=');
            while (index > 0 && index < 22)
            {
                base64Buffer[index] = base64Buffer[index] switch
                {
                    '+' => '-',
                    '/' => '_',
                    _ => throw new UnreachableException("Unexpected character encounter generating URI-safe Base64 string.")
                };
                index = base64Buffer.IndexOfAny('+', '/', '=');
            }
            return new string(base64Buffer[..22]);
        }

        private readonly IDataService dataService;
        private readonly ITermService termService;
        private readonly IScheduleService undergradScheduleService;
        private readonly IScheduleService postgradScheduleService;
        private readonly IStoredCredentialEncryptionService storedEncryptionService;
        private readonly ICalendarService calendarService;
        private readonly ILocalizationService localizationService;
    }

    internal sealed class SubscriptionDeleteFunction
    {
        public SubscriptionDeleteFunction(
            IDataService dataService,
            UndergraduateScheduleService undergradScheduleService,
            PostgraduateScheduleService postgradScheduleService,
            ILocalizationService localizationService)
        {
            this.dataService = dataService;
            this.undergradScheduleService = undergradScheduleService;
            this.postgradScheduleService = postgradScheduleService;
            this.localizationService = localizationService;
        }

        [Function("Subscription_Delete")]
        public async Task<HttpResponseData> RunDeleteAsync([HttpTrigger(AuthorizationLevel.Function, "post", Route = "subscription/delete")] HttpRequestData req)
        {
            ILogger log = req.FunctionContext.GetFunctionNamedLogger();
            Credential credential = await req.GetCredentialAsync();
            if (credential == null)
            {
                return req.CreateResponse(HttpStatusCode.BadRequest);
            }
            else if (!credential.Username.StartsWith("20", StringComparison.Ordinal))
            {
                var response = new CquSchedule.Models.Response<int>(localizationService.GetString("UsernameInvalid"));
                return StatusOnlyResponseSerializerContext.Default.GetSerializedResponse(req, response, HttpStatusCode.BadRequest);
            }

            IScheduleService scheduleService;
            switch (credential.Username.Length)
            {
                case 8:
                    scheduleService = undergradScheduleService;
                    break;
                case 12:
                case 13:
                    scheduleService = postgradScheduleService;
                    break;
                default:
                    var response = new CquSchedule.Models.Response<int>(localizationService.GetString("UsernameInvalid"));
                    return StatusOnlyResponseSerializerContext.Default.GetSerializedResponse(req, response, HttpStatusCode.BadRequest);
            }

            try
            {
                await scheduleService.SignInAsync(credential.Username, credential.Password);
            }
            catch (AuthenticationException ex)
            {
                string message;
                HttpStatusCode statusCode = HttpStatusCode.Unauthorized;
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
                else if (ex.Result == AuthenticationResult.ConnectionFailed)
                {
                    message = localizationService.GetString("ServiceErrorConnectionFailure");
                }
                else if (ex.InnerException is SocketException)
                {
                    message = localizationService.GetString("ServerDeniedConnection");
                }
                else
                {
                    log.LogError(ex, "Unexpected response while authenticating user.");
                    message = localizationService.GetString("AuthErrorCannotDelete");
                    statusCode = HttpStatusCode.ServiceUnavailable;
                }
                var response = new DL444.CquSchedule.Models.Response<int>(message);
                return StatusOnlyResponseSerializerContext.Default.GetSerializedResponse(req, response, statusCode);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to authenticate user.");
                var response = new DL444.CquSchedule.Models.Response<int>(localizationService.GetString("ServiceErrorCannotDelete"));
                return StatusOnlyResponseSerializerContext.Default.GetSerializedResponse(req, response, HttpStatusCode.ServiceUnavailable);
            }

            bool success = await dataService.DeleteUserAsync(credential.Username);
            if (success)
            {
                var response = new DL444.CquSchedule.Models.Response<int>(true, default, localizationService.GetString("UserDeleteSuccess"));
                return StatusOnlyResponseSerializerContext.Default.GetSerializedResponse(req, response);
            }
            else
            {
                log.LogError("Failed to delete user. Username: {username}", credential.Username);
                var response = new DL444.CquSchedule.Models.Response<int>(localizationService.GetString("ServiceErrorCannotDelete"));
                return StatusOnlyResponseSerializerContext.Default.GetSerializedResponse(req, response, HttpStatusCode.ServiceUnavailable);
            }
        }

        private readonly IDataService dataService;
        private readonly IScheduleService undergradScheduleService;
        private readonly IScheduleService postgradScheduleService;
        private readonly ILocalizationService localizationService;
    }
}
