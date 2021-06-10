using DL444.CquSchedule.Models;
using Newtonsoft.Json;

namespace DL444.CquSchedule.Backend.Models
{
    internal struct ServiceStatusContainer : ICosmosResource
    {
        [JsonProperty("id")]
        public string Id => "Status";
        public string PartitionKey => "Status";

        public ServiceStatus Status { get; set; }
    }
}
