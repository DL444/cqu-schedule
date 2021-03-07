using System;

namespace DL444.CquSchedule.Backend.Exceptions
{
    internal class UpstreamRequestException : Exception
    {
        public UpstreamRequestException() { }
        public UpstreamRequestException(string message) : base(message) { }
        public UpstreamRequestException(string message, Exception innerException) : base(message, innerException) { }

        public string ErrorDescription { get; set; }
    }
}
