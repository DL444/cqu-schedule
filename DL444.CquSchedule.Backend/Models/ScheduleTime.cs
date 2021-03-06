using System;

namespace DL444.CquSchedule.Backend.Models
{
    public struct ScheduleTime
    {
        public ScheduleTime(TimeSpan startOffset, TimeSpan endOffset)
        {
            StartOffset = startOffset;
            EndOffset = endOffset;
        }

        public TimeSpan StartOffset { get; set; }
        public TimeSpan EndOffset { get; set; }
        public TimeSpan Duration => EndOffset - StartOffset;
    }
}
