namespace DL444.CquSchedule.Backend.Models
{
    internal interface IStatusResource
    {
        RecordStatus RecordStatus { get; set; }
    }

    internal enum RecordStatus
    {
        UpToDate = 0,
        StaleAuthError = 1,
        StaleUpstreamError = 2
    }
}
