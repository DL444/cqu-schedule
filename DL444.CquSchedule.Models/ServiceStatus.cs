using System;

namespace DL444.CquSchedule.Models
{
    public struct ServiceStatus
    {
        public StatusLevel CurrentStatusLevel { get; set; }
        public string Description { get; set; }
        public Incident[] Incidents { get; set; }
    }

    public struct Incident
    {
        public StatusLevel Level { get; set; }
        public string Description { get; set; }
        public string Details { get; set; }
        public bool Resolved { get; set; }
        public DateTimeOffset StartTime { get; set; }
        public DateTimeOffset EndTime { get; set; }
    }

    public enum StatusLevel
    {
        Ok = 0,
        Information = 1,
        Warning = 2,
        Error = 3
    }
}
