using System;
using System.Text.Json.Serialization;

namespace DL444.CquSchedule.Backend.Models
{
    internal struct Term : ICosmosResource
    {
        [JsonPropertyName("id")]
        public string Id => "Term";
        public string PartitionKey => "Term";

        public DateTimeOffset StartDate { get; set; }
        public DateTimeOffset EndDate { get; set; }
    }
}
