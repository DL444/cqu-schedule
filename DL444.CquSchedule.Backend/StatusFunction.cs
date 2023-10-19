using System;
using System.Net;
using System.Threading.Tasks;
using DL444.CquSchedule.Backend.Extensions;
using DL444.CquSchedule.Backend.Models;
using DL444.CquSchedule.Backend.Services;
using DL444.CquSchedule.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace DL444.CquSchedule.Backend
{
    internal sealed class StatusFunction
    {
        public StatusFunction(IDataService dataService, ILocalizationService localizationService)
        {
            this.dataService = dataService;
            this.localizationService = localizationService;
        }

        [Function("Status")]
        public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequestData req)
        {
            ILogger log = req.FunctionContext.GetFunctionNamedLogger();
            try
            {
                ServiceStatus status = (await dataService.GetServiceStatusAsync()).Status;
                return ServiceStatusResponseSerializerContext.Default.GetSerializedResponse(req, new Response<ServiceStatus>(status));
            }
            catch (Microsoft.Azure.Cosmos.CosmosException ex)
            {
                log.LogError(ex, "Failed to fetch service status from database. Status {status}", ex.StatusCode);
                var response = new Response<ServiceStatus>(localizationService.GetString("ServiceErrorCannotGetStatus"));
                return ServiceStatusResponseSerializerContext.Default.GetSerializedResponse(req, response, HttpStatusCode.ServiceUnavailable);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to fetch service status.");
                var response = new Response<ServiceStatus>(localizationService.GetString("ServiceErrorCannotGetStatus"));
                return ServiceStatusResponseSerializerContext.Default.GetSerializedResponse(req, response, HttpStatusCode.ServiceUnavailable);
            }
        }

        private readonly IDataService dataService;
        private readonly ILocalizationService localizationService;
    }
}
