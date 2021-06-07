namespace DL444.CquSchedule.Backend.Models
{
    internal struct ScheduleRefreshInput
    {
        public string Username { get; set; }
        public bool TermRefreshed => !string.IsNullOrEmpty(SessionTermId);
        public string SessionTermId { get; set; }
    }
}
