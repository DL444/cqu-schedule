using System;
using System.Collections.Generic;
using DL444.CquSchedule.Backend.Models;
using Microsoft.Extensions.Configuration;

namespace DL444.CquSchedule.Backend.Services
{
    internal interface IWellknownDataService
    {
        IList<ScheduleTime> Schedule { get; }
    }

    internal class WellknownDataService : IWellknownDataService
    {
        public WellknownDataService(IConfiguration config)
        {
            List<ScheduleTime> scheduleItems = new List<ScheduleTime>();
            foreach (IConfigurationSection scheduleCfgItem in config.GetSection("Wellknown:Schedule").GetChildren())
            {
                string startTime = scheduleCfgItem.GetValue<string>("StartOffset");
                string endTime = scheduleCfgItem.GetValue<string>("EndOffset");
                scheduleItems.Add(new ScheduleTime(TimeSpan.Parse(startTime), TimeSpan.Parse(endTime)));
            }
            Schedule = scheduleItems.AsReadOnly();
        }

        public IList<ScheduleTime> Schedule { get; }
    }
}
