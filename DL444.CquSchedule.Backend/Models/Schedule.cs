using System;
using System.Collections.Generic;
using System.Linq;
using DL444.CquSchedule.Backend.Extensions;
using Newtonsoft.Json;

namespace DL444.CquSchedule.Backend.Models
{
    internal struct Schedule : IStatusResource, ICosmosResource, IEquatable<Schedule>
    {
        public Schedule(string user)
        {
            User = user;
            RecordStatus = RecordStatus.UpToDate;
            Weeks = new List<ScheduleWeek>();
            Exams = new List<ExamEntry>();
        }

        [JsonProperty("id")]
        public string Id => $"Schedule-{User}";
        public string PartitionKey => User;
        public RecordStatus RecordStatus { get; set; }

        public string User { get; set; }
        public List<ScheduleWeek> Weeks { get; set; }
        public List<ExamEntry> Exams { get; set; }

        public void AddEntry(int week, ScheduleEntry entry)
        {
            ScheduleWeek scheduleWeek = Weeks.FirstOrDefault(x => x.WeekNumber == week);
            if (scheduleWeek.WeekNumber == 0)
            {
                scheduleWeek = new ScheduleWeek(week);
                Weeks.Add(scheduleWeek);
            }
            scheduleWeek.AddEntry(entry);
        }

        public void AddExam(ExamEntry exam) => Exams.AddOrReplace(exam.EntryKey, exam, exams, (existingItem, newItem) => new ExamEntry()
        {
            Name = existingItem.Name,
            Room = string.IsNullOrEmpty(existingItem.Room) ? newItem.Room : existingItem.Room,
            SimplifiedRoom = string.IsNullOrEmpty(existingItem.SimplifiedRoom) ? newItem.SimplifiedRoom : existingItem.SimplifiedRoom,
            Seat = existingItem.Seat == 0 ? newItem.Seat : existingItem.Seat,
            StartTime = existingItem.StartTime,
            EndTime = existingItem.EndTime
        });

        public bool Equals(Schedule other)
        {
            if (!string.Equals(User, other.User, StringComparison.Ordinal))
            {
                return false;
            }
            if ((Weeks == null) ^ (other.Weeks == null) || (Exams == null) ^ (other.Exams == null))
            {
                return false;
            }
            if (Weeks != null && !Weeks.SequenceEqual(other.Weeks))
            {
                return false;
            }
            if (Exams != null && !Exams.SequenceEqual(other.Exams))
            {
                return false;
            }
            return true;
        }

        public override bool Equals(object obj) => obj is Schedule other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(User, Weeks);

        public static bool operator ==(Schedule l, Schedule r) => l.Equals(r);
        public static bool operator !=(Schedule l, Schedule r) => !(l == r);

        private readonly Dictionary<ExamEntry.Key, ExamEntry> exams = [];
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

        public void AddEntry(ScheduleEntry entry)
            => Entries.AddOrReplace(entry.EntryKey, entry, entries, (existingItem, newItem) => new ScheduleEntry()
            {
                Name = existingItem.Name,
                Lecturer = string.IsNullOrEmpty(existingItem.Lecturer) ? newItem.Lecturer : existingItem.Lecturer,
                Room = string.IsNullOrEmpty(existingItem.Room) ? newItem.Room : existingItem.Room,
                SimplifiedRoom = string.IsNullOrEmpty(existingItem.SimplifiedRoom) ? newItem.SimplifiedRoom : existingItem.SimplifiedRoom,
                DayOfWeek = existingItem.DayOfWeek,
                StartSession = existingItem.StartSession,
                EndSession = existingItem.EndSession
            });

        public bool Equals(ScheduleWeek other)
        {
            if (WeekNumber != other.WeekNumber || (Entries == null) ^ (other.Entries == null) || Entries.Count != other.Entries.Count)
            {
                return false;
            }

            // Since there should be only a handful of items in the list this complexity should be acceptable.
            // Otherwise we need to copy and sort the lists.
            return Entries == null || Entries.All(x => other.Entries.Contains(x));
        }

        public override bool Equals(object obj) => obj is ScheduleWeek other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(WeekNumber, Entries);

        public static bool operator ==(ScheduleWeek l, ScheduleWeek r) => l.Equals(r);
        public static bool operator !=(ScheduleWeek l, ScheduleWeek r) => !(l == r);

        private readonly Dictionary<ScheduleEntry.Key, ScheduleEntry> entries = [];
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

        public readonly Key EntryKey => new(Name, DayOfWeek, StartSession, EndSession);

        public bool Equals(ScheduleEntry other) =>
            string.Equals(Name, other.Name, StringComparison.Ordinal)
                && DayOfWeek == other.DayOfWeek
                && StartSession == other.StartSession
                && EndSession == other.EndSession
                && string.Equals(Lecturer, other.Lecturer, StringComparison.Ordinal)
                && string.Equals(Room, other.Room, StringComparison.Ordinal)
                && string.Equals(SimplifiedRoom, other.SimplifiedRoom, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is ScheduleEntry other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Name, Lecturer, Room, SimplifiedRoom, DayOfWeek, StartSession, EndSession);

        public static bool operator ==(ScheduleEntry l, ScheduleEntry r) => l.Equals(r);
        public static bool operator !=(ScheduleEntry l, ScheduleEntry r) => !(l == r);

        public record Key(string Name, int DayOfWeek, int StartSession, int EndSession);
    }

    public struct ExamEntry : IEquatable<ExamEntry>
    {
        public string Name { get; set; }
        public string Room { get; set; }
        public string SimplifiedRoom { get; set; }
        public int Seat { get; set; }
        public DateTimeOffset StartTime { get; set; }
        public DateTimeOffset EndTime { get; set; }

        public readonly Key EntryKey => new Key(Name, StartTime, EndTime);

        public bool Equals(ExamEntry other) =>
            string.Equals(Name, other.Name, StringComparison.Ordinal)
                && StartTime == other.StartTime
                && EndTime == other.EndTime
                && Seat == other.Seat
                && string.Equals(Room, other.Room, StringComparison.Ordinal)
                && string.Equals(SimplifiedRoom, other.SimplifiedRoom, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is ExamEntry other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Name, Room, SimplifiedRoom, Seat, StartTime, EndTime);

        public static bool operator ==(ExamEntry l, ExamEntry r) => l.Equals(r);
        public static bool operator !=(ExamEntry l, ExamEntry r) => !(l == r);

        public record Key(string Name, DateTimeOffset StartTime, DateTimeOffset EndTime);
    }
}
