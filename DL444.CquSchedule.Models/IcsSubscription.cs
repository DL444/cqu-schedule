namespace DL444.CquSchedule.Models
{
    public struct IcsSubscription
    {
        public IcsSubscription(string subscriptionId, string icsContent)
        {
            SubscriptionId = subscriptionId;
            IcsContent = icsContent;
        }

        public string SubscriptionId { get; set; }
        public string IcsContent { get; set; }
    }
}
