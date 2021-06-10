using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using DL444.CquSchedule.Backend.Services;
using DL444.CquSchedule.Models;

namespace DL444.CquSchedule.Backend
{
    internal class StatusFunction
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
                return new OkObjectResult(new Response<ServiceStatus>(status));
            }
            catch (Microsoft.Azure.Cosmos.CosmosException ex)
            {
                log.LogError(ex, "Failed to fetch service status from database. Status {status}", ex.StatusCode);
                return new ObjectResult(new Response<ServiceStatus>(localizationService.GetString("ServiceErrorCannotGetStatus")))
                {
                    StatusCode = 503
                };
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to fetch service status.");
                return new ObjectResult(new Response<ServiceStatus>(localizationService.GetString("ServiceErrorCannotGetStatus")))
                {
                    StatusCode = 503
                };
            }
        }

        private readonly IDataService dataService;
        private readonly ILocalizationService localizationService;
    }
}
