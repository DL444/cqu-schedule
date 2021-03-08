using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace DL444.CquSchedule.Backend.Models
{
    internal struct Schedule : IStatusResource, ICosmosResource
    {
        public Schedule(string user)
        {
            User = user;
            RecordStatus = RecordStatus.UpToDate;
            Weeks = new List<ScheduleWeek>();
        }

        [JsonPropertyName("id")]
        public string Id => $"Schedule-{User}";
        public string PartitionKey => User;
        public RecordStatus RecordStatus { get; set; }

        public string User { get; set; }
        public List<ScheduleWeek> Weeks { get; set; }

        public void AddEntry(int week, ScheduleEntry entry)
        {
            ScheduleWeek scheduleWeek = Weeks.FirstOrDefault(x => x.WeekNumber == week);
            if (scheduleWeek.WeekNumber == 0)
            {
                scheduleWeek = new ScheduleWeek(week);
                Weeks.Add(scheduleWeek);
            }
            scheduleWeek.Entries.Add(entry);
        }
    }

    public struct ScheduleWeek
    {
        public ScheduleWeek(int weekNumber)
        {
            WeekNumber = weekNumber;
            Entries = new List<ScheduleEntry>();
        }

        public int WeekNumber { get; set; }
        public List<ScheduleEntry> Entries { get; set; }
    }

    public struct ScheduleEntry
    {
        public string Name { get; set; }
        public string Lecturer { get; set; }
        public string Room { get; set; }
        public string SimplifiedRoom { get; set; }
        public int DayOfWeek { get; set; }
        public int StartSession { get; set; }
        public int EndSession { get; set; }
    }
}
