namespace DL444.CquSchedule.Web.Models
{
    public sealed class HelpPageModel
    {
        public AddMethod AddMethod { get; set; }
        public CalendarServiceType ServiceType { get; set; }
    }

    public enum AddMethod
    {
        Unspecified,
        Subscription,
        File
    }

    public enum CalendarServiceType
    {
        Unspecified,
        Apple,
        Google,
        Outlook,
        Others
    }

}
