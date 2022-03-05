using Newtonsoft.Json;

namespace DL444.CquSchedule.Backend.Models
{
    internal struct User : ICosmosResource
    {
        [JsonProperty("id")]
        public string Id => $"User-{Username}";
        public string PartitionKey => "User";
        public string Username { get; set; }
        public string Password { get; set; }
        public string KeyId { get; set; }
        public string LastRotatedPassword { get; set; }
        public string LastRotatedKeyId { get; set; }
        public string SubscriptionId { get; set; }
        public UserType UserType { get; set; }
    }
}
