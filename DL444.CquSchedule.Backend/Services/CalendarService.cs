using System;
using DL444.CquSchedule.Backend.Models;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;

namespace DL444.CquSchedule.Backend.Services
{
    internal interface ICalendarService
    {
        string GetCalendar(Schedule schedule, int remindTime = 15);
        string GetEmptyCalendar();
    }

    internal class CalendarService : ICalendarService
    {
        public CalendarService(IWellknownDataService wellknown, ILocalizationService locService)
        {
            this.wellknown = wellknown;
            this.locService = locService;
        }

        public string GetCalendar(Schedule schedule, int remindTime)
        {
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
                        DtStart = GetTime(week.WeekNumber, entry.DayOfWeek, startTime),
                        DtEnd = GetTime(week.WeekNumber, entry.DayOfWeek, endTime),
                        Location = entry.Room,
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

        private CalDateTime GetTime(int week, int dayOfWeek, TimeSpan time)
        {
            int daysSinceTermStart = (week - 1) * 7 + (dayOfWeek - 1);
            DateTimeOffset dateTime = wellknown.TermStartDate.AddDays(daysSinceTermStart).Add(time);
            return new CalDateTime(dateTime.UtcDateTime);
        }

        private readonly IWellknownDataService wellknown;
        private readonly ILocalizationService locService;
    }
}
