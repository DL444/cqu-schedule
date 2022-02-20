using System.Text.Json.Serialization;

namespace DL444.CquSchedule.Backend.Models
{
    internal struct UpstreamExamResponseModel
    {
        [JsonPropertyName("status")]
        public string Status { get; set; }
        [JsonPropertyName("msg")]
        public string Message { get; set; }
        [JsonPropertyName("data")]
        public ExamDataModel Data { get; set; }
    }

    internal struct ExamDataModel
    {
        [JsonPropertyName("content")]
        public ExamContentEntry[] Content { get; set; }
        [JsonPropertyName("totalPages")]
        public int TotalPages { get; set; }
    }

    internal struct ExamContentEntry
    {
        [JsonPropertyName("courseName")]
        public string Name { get; set; }
        [JsonPropertyName("roomName")]
        public string Room { get; set; }
        [JsonPropertyName("seatNum")]
        public string Seat { get; set; }
        [JsonPropertyName("examDate")]
        public string Date { get; set; }
        [JsonPropertyName("startTime")]
        public string StartTime { get; set; }
        [JsonPropertyName("endTime")]
        public string EndTime { get; set; }
    }
}
