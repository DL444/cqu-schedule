using System.Text.Json.Serialization;

namespace DL444.CquSchedule.Backend.Models
{
    internal struct UpstreamTermResponseModel
    {
        [JsonPropertyName("status")]
        public string Status { get; set; }
        [JsonPropertyName("msg")]
        public string Message { get; set; }
        [JsonPropertyName("data")]
        public TermDataModel Data { get; set; }
    }

    internal struct TermDataModel
    {
        [JsonPropertyName("beginDate")]
        public string StartDateString { get; set; }
        [JsonPropertyName("endDate")]
        public string EndDateString { get; set; }
    }
}
