using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using DL444.CquSchedule.Backend.Models;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using Microsoft.Extensions.Configuration;

namespace DL444.CquSchedule.Backend.Services
{
    [Flags]
    public enum CalenderEventCategories
    {
        None = 0,
        Courses = 1 << 0,
        Exams = 1 << 1,
        All = int.MaxValue
    }

    internal interface ICalendarService
    {
        int VacationCalendarServeDays { get; }

        string GetCalendar(string username, Term currentTerm, Schedule schedule, CalenderEventCategories eventCategories = CalenderEventCategories.All, int remindTime = 15);
        string GetEmptyCalendar();
    }

    internal sealed class CalendarService : ICalendarService
    {
        public CalendarService(IConfiguration config, IWellknownDataService wellknown, ILocalizationService locService)
        {
            VacationCalendarServeDays = config.GetValue("Calendar:VacationServeDays", 3);
            this.wellknown = wellknown;
            this.locService = locService;
        }

        public int VacationCalendarServeDays { get; }

        public string GetCalendar(string username, Term currentTerm, Schedule schedule, CalenderEventCategories eventCategories, int remindTime)
        {
            if (DateTimeOffset.Now > currentTerm.EndDate.AddDays(VacationCalendarServeDays)
                || DateTimeOffset.Now < currentTerm.StartDate.AddDays(-VacationCalendarServeDays))
            {
                return GetEmptyCalendar();
            }

            Calendar calendar = new Calendar();
            StringBuilder descriptionBuilder = new StringBuilder();
            if (eventCategories.HasFlag(CalenderEventCategories.Courses) && schedule.Weeks != null)
            {
                foreach (var week in schedule.Weeks)
                {
                    foreach (ScheduleEntry entry in week.Entries)
                    {
                        if (entry.StartSession > wellknown.Schedule.Count)
                        {
                            continue;
                        }
                        calendar.Events.Add(GetCalendarEvent(username, entry, currentTerm, week, remindTime, descriptionBuilder));
                    }
                }
            }

            if (eventCategories.HasFlag(CalenderEventCategories.Exams) && schedule.Exams != null)
            {
                foreach (var exam in schedule.Exams)
                {
                    calendar.Events.Add(GetCalendarEvent(username, exam, remindTime));
                }
            }

            return new CalendarSerializer(calendar).SerializeToString();
        }

        public string GetEmptyCalendar() => new CalendarSerializer(new Calendar()).SerializeToString();

        private CalendarEvent GetCalendarEvent(string username, ScheduleEntry entry, Term currentTerm, ScheduleWeek week, int remindTime, StringBuilder descriptionBuilder)
        {
            TimeSpan startTime = wellknown.Schedule[entry.StartSession - 1].StartOffset;
            int endSession = Math.Min(entry.EndSession, wellknown.Schedule.Count);
            TimeSpan endTime = wellknown.Schedule[endSession - 1].EndOffset;

            descriptionBuilder.Clear();
            bool positionSet = !string.IsNullOrWhiteSpace(entry.Position);
            bool roomSet = !string.IsNullOrWhiteSpace(entry.Room);
            bool appendRoom = positionSet || (roomSet && !entry.Room.Equals(entry.SimplifiedRoom, StringComparison.Ordinal));
            bool appendLecturer = entry.Lecturer != null;
            if (appendRoom)
            {
                string fullRoom = (positionSet, roomSet) switch
                {
                    (true, true) => $"{entry.Room}-{entry.Position}",
                    (true, false) => entry.Position,
                    (false, true) => entry.Room,
                    _ => throw new UnreachableException()
                };
                descriptionBuilder.Append(locService.GetString("CalendarRoom", locService.DefaultCulture, fullRoom));
            }
            if (appendRoom && appendLecturer)
            {
                descriptionBuilder.Append('\n');
            }
            if (appendLecturer)
            {
                descriptionBuilder.Append(locService.GetString("CalendarLecturer", locService.DefaultCulture, entry.Lecturer));
            }

            var calendarEvent = new CalendarEvent()
            {
                Summary = entry.Name,
                DtStart = GetTime(currentTerm.StartDate, week.WeekNumber, entry.DayOfWeek, startTime),
                DtEnd = GetTime(currentTerm.StartDate, week.WeekNumber, entry.DayOfWeek, endTime),
                Location = string.IsNullOrWhiteSpace(entry.SimplifiedRoom) ? null : entry.SimplifiedRoom,
                Description = descriptionBuilder.ToString(),
                Alarms = {
                    new Alarm()
                    {
                        Trigger = new Trigger(new TimeSpan(0, -remindTime, 0)),
                        Action = "DISPLAY",
                        Description = entry.Name
                    }
                }
            };
            calendarEvent.Uid = GetUid(calendarEvent, username);
            return calendarEvent;
        }

        private static CalendarEvent GetCalendarEvent(string username, ExamEntry exam, int remindTime)
        {
            bool roomSimplified = exam.Room != null && !exam.Room.Equals(exam.SimplifiedRoom, StringComparison.Ordinal);
            var calendarEvent = new CalendarEvent()
            {
                Summary = exam.Name,
                DtStart = new CalDateTime(exam.StartTime.UtcDateTime),
                DtEnd = new CalDateTime(exam.EndTime.UtcDateTime),
                Location = exam.Seat > 0 ? $"{exam.SimplifiedRoom}@{exam.Seat}" : exam.SimplifiedRoom,
                Description = roomSimplified ? exam.Room : string.Empty,
                Alarms = {
                    new Alarm()
                    {
                        Trigger = new Trigger(new TimeSpan(0, -remindTime, 0)),
                        Action = "DISPLAY",
                        Description = exam.Name
                    }
                }
            };
            calendarEvent.Uid = GetUid(calendarEvent, username);
            return calendarEvent;
        }

        private static CalDateTime GetTime(DateTimeOffset termStartDate, int week, int dayOfWeek, TimeSpan time)
        {
            int daysSinceTermStart = (week - 1) * 7 + (dayOfWeek - 1);
            DateTimeOffset dateTime = termStartDate.AddDays(daysSinceTermStart).Add(time);
            return new CalDateTime(dateTime.UtcDateTime);
        }

        private static string GetUid(CalendarEvent calendarEvent, string ancillaryIdentifier)
        {
            // UIDs must be unique across events and temporally persistent at the same time,
            // or clients might briefly show duplicated events during refresh.
            // This property choice ensures that no duplicated UIDs will be generated even if
            // the user has conflicting schedules, while minimizing the likelihood of UID changes
            // in the event of schedule updates (summary and time rarely change).
            var idSource = $"{ancillaryIdentifier}, {calendarEvent.Summary}, {calendarEvent.DtStart}, {calendarEvent.DtEnd}";
            return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(idSource)));
        }

        private readonly IWellknownDataService wellknown;
        private readonly ILocalizationService locService;
    }
}
