using System;
using System.Threading.Tasks;
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
    internal sealed class StatusFunction
    {
        public StatusFunction(IDataService dataService, ILocalizationService localizationService)
        {
            this.dataService = dataService;
            this.localizationService = localizationService;
        }

        [FunctionName("Status")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequest req,
            ILogger log)
        {
            try
            {
                ServiceStatus status = (await dataService.GetServiceStatusAsync()).Status;
                return ServiceStatusResponseSerializerContext.Default.GetSerializedResponse(new Response<ServiceStatus>(status));
            }
            catch (Microsoft.Azure.Cosmos.CosmosException ex)
            {
                log.LogError(ex, "Failed to fetch service status from database. Status {status}", ex.StatusCode);
                var response = new Response<ServiceStatus>(localizationService.GetString("ServiceErrorCannotGetStatus"));
                return ServiceStatusResponseSerializerContext.Default.GetSerializedResponse(response, 503);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to fetch service status.");
                var response = new Response<ServiceStatus>(localizationService.GetString("ServiceErrorCannotGetStatus"));
                return ServiceStatusResponseSerializerContext.Default.GetSerializedResponse(response, 503);
            }
        }

        private readonly IDataService dataService;
        private readonly ILocalizationService localizationService;
    }
}
