using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace DL444.CquSchedule.Backend.Models
{
    internal struct Schedule : IStatusResource, ICosmosResource, IEquatable<Schedule>
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

        public bool Equals(Schedule other)
        {
            if (!string.Equals(User, other.User, StringComparison.Ordinal) || (Weeks == null) ^ (other.Weeks == null) || Weeks.Count != other.Weeks.Count)
            {
                return false;
            }
            if (Weeks == null)
            {
                return true;
            }
            for (int i = 0; i < Weeks.Count; i++)
            {
                if (Weeks[i] != other.Weeks[i])
                {
                    return false;
                }
            }
            return true;
        }

        public override bool Equals(object obj) => obj is Schedule other ? Equals(other) : false;

        public override int GetHashCode() => HashCode.Combine(User, Weeks);

        public static bool operator == (Schedule l, Schedule r) => l.Equals(r);
        public static bool operator != (Schedule l, Schedule r) => !(l == r);
    }

    public struct ScheduleWeek : IEquatable<ScheduleWeek>
    {
        public ScheduleWeek(int weekNumber)
        {
            WeekNumber = weekNumber;
            Entries = new List<ScheduleEntry>();
        }

        public int WeekNumber { get; set; }
        public List<ScheduleEntry> Entries { get; set; }

        public bool Equals(ScheduleWeek other)
        {
            if (WeekNumber != other.WeekNumber || (Entries == null) ^ (other.Entries == null) || Entries.Count != other.Entries.Count)
            {
                return false;
            }

            // Since there should be only a handful of items in the list this complexity should be acceptable.
            // Otherwise we need to copy and sort the lists.
            return Entries == null ? true : Entries.All(x => other.Entries.Contains(x));
        }

        public override bool Equals(object obj) => obj is ScheduleWeek other ? Equals(other) : false;

        public override int GetHashCode() => HashCode.Combine(WeekNumber, Entries);
        
        public static bool operator == (ScheduleWeek l, ScheduleWeek r) => l.Equals(r);
        public static bool operator != (ScheduleWeek l, ScheduleWeek r) => !(l == r);
    }

    public struct ScheduleEntry : IEquatable<ScheduleEntry>
    {
        public string Name { get; set; }
        public string Lecturer { get; set; }
        public string Room { get; set; }
        public string SimplifiedRoom { get; set; }
        public int DayOfWeek { get; set; }
        public int StartSession { get; set; }
        public int EndSession { get; set; }

        public bool Equals(ScheduleEntry other) => 
            string.Equals(Name, other.Name, StringComparison.Ordinal)
                && DayOfWeek == other.DayOfWeek
                && StartSession == other.StartSession
                && EndSession == other.EndSession
                && string.Equals(Lecturer, other.Lecturer, StringComparison.Ordinal)
                && string.Equals(Room, other.Room, StringComparison.Ordinal)
                && string.Equals(SimplifiedRoom, other.SimplifiedRoom, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is ScheduleEntry other ? Equals(other) : false;

        public override int GetHashCode() => HashCode.Combine(Name, Lecturer, Room, SimplifiedRoom, DayOfWeek, StartSession, EndSession);

        public static bool operator == (ScheduleEntry l, ScheduleEntry r) => l.Equals(r);
        public static bool operator != (ScheduleEntry l, ScheduleEntry r) => !(l == r);
    }
}
