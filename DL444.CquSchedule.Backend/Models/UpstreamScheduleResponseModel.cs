using System.Text.Json.Serialization;

namespace DL444.CquSchedule.Backend.Models
{
    internal struct UpstreamScheduleResponseModel
    {
        [JsonPropertyName("status")]
        public string Status { get; set; }
        [JsonPropertyName("msg")]
        public string Message { get; set; }
        [JsonPropertyName("data")]
        public ScheduleDataEntry[] Data { get; set; }
    }

    internal struct ScheduleDataEntry
    {
        [JsonPropertyName("courseName")]
        public string Name { get; set; }
        [JsonPropertyName("roomName")]
        public string Room { get; set; }
        [JsonPropertyName("teachingWeek")]
        public string Weeks { get; set; }
        [JsonPropertyName("weekDay")]
        public string DayOfWeek { get; set; }
        [JsonPropertyName("period")]
        public string Session { get; set; }
        [JsonPropertyName("classTimetableInstrVOList")]
        public LecturerEntry[] Lecturers { get; set; }
        [JsonPropertyName("classType")]
        public string ClassType { get; set; }
    }

    internal struct LecturerEntry
    {
        [JsonPropertyName("instructorName")]
        public string Lecturer { get; set; }
    }
}
