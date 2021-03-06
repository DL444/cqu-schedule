using System.Text.Json.Serialization;

namespace DL444.CquSchedule.Backend.Models
{
    internal struct User : ICosmosResource
    {
        [JsonPropertyName("id")]
        public string Id => $"User-{Username}";
        public string PartitionKey => "User";
        public string Username { get; set; }
        public string Password { get; set; }
        public string Iv { get; set; }
        public string SubscriptionId { get; set; }
    }
}
