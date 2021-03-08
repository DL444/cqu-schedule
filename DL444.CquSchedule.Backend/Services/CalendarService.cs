using System;
using DL444.CquSchedule.Backend.Models;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using Microsoft.Extensions.Configuration;

namespace DL444.CquSchedule.Backend.Services
{
    internal interface ICalendarService
    {
        int VacationCalendarServeDays { get; }

        string GetCalendar(Term currentTerm, Schedule schedule, int remindTime = 15);
        string GetEmptyCalendar();
    }

    internal class CalendarService : ICalendarService
    {
        public CalendarService(IConfiguration config, IWellknownDataService wellknown, ILocalizationService locService)
        {
            VacationCalendarServeDays = config.GetValue("Calendar:VacationServeDays", 3);
            this.wellknown = wellknown;
            this.locService = locService;
        }

        public int VacationCalendarServeDays { get; }

        public string GetCalendar(Term currentTerm, Schedule schedule, int remindTime)
        {
            if (DateTimeOffset.Now > currentTerm.EndDate.AddDays(VacationCalendarServeDays)
                || DateTimeOffset.Now < currentTerm.StartDate.AddDays(-VacationCalendarServeDays))
            {
                return GetEmptyCalendar();
            }

            Calendar calendar = new Calendar();
            foreach (var week in schedule.Weeks)
            {
                foreach (ScheduleEntry entry in week.Entries)
                {
                    if (entry.StartSession > wellknown.Schedule.Count)
                    {
                        continue;
                    }
                    TimeSpan startTime = wellknown.Schedule[entry.StartSession - 1].StartOffset;
                    int endSession = Math.Min(entry.EndSession, wellknown.Schedule.Count);
                    TimeSpan endTime = wellknown.Schedule[endSession - 1].EndOffset;
                    CalendarEvent calEvent = new CalendarEvent()
                    {
                        Summary = entry.Name,
                        DtStart = GetTime(currentTerm.StartDate, week.WeekNumber, entry.DayOfWeek, startTime),
                        DtEnd = GetTime(currentTerm.StartDate, week.WeekNumber, entry.DayOfWeek, endTime),
                        Location = entry.SimplifiedRoom,
                        Description = entry.Lecturer == null ? null : locService.GetString("CalendarLecturer", locService.DefaultCulture, entry.Lecturer),
                        Alarms = {
                            new Alarm()
                            {
                                Trigger = new Trigger(new TimeSpan(0, -remindTime, 0))
                            }
                        }
                    };
                    calendar.Events.Add(calEvent);
                }
            }

            return new CalendarSerializer(calendar).SerializeToString();
        }

        public string GetEmptyCalendar() => new CalendarSerializer(new Calendar()).SerializeToString();

        private CalDateTime GetTime(DateTimeOffset termStartDate, int week, int dayOfWeek, TimeSpan time)
        {
            int daysSinceTermStart = (week - 1) * 7 + (dayOfWeek - 1);
            DateTimeOffset dateTime = termStartDate.AddDays(daysSinceTermStart).Add(time);
            return new CalDateTime(dateTime.UtcDateTime);
        }

        private readonly IWellknownDataService wellknown;
        private readonly ILocalizationService locService;
    }
}
