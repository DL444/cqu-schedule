using System.Text.Json.Serialization;

namespace DL444.CquSchedule.Backend.Models
{
    internal struct UpstreamScheduleResponseModel
    {
        [JsonPropertyName("classTimetableVOList")]
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
        [JsonPropertyName("instructorName")]
        public string LecturersNotation { get; set; }
        [JsonPropertyName("classType")]
        public string ClassType { get; set; }
    }
}
