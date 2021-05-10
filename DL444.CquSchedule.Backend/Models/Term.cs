using System;
using Newtonsoft.Json;

namespace DL444.CquSchedule.Backend.Models
{
    internal struct Term : ICosmosResource
    {
        [JsonProperty("id")]
        public string Id => "Term";
        public string PartitionKey => "Term";

        public DateTimeOffset StartDate { get; set; }
        public DateTimeOffset EndDate { get; set; }
    }
}
