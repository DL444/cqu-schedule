using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DL444.CquSchedule.Backend.Models
{
    internal struct UpstreamTermListResponseModel
    {
        [JsonPropertyName("curSessionId")]
        public string CurrentTerm { get; set; }

        [JsonPropertyName("sessionFinder")]
        public List<TermListItemModel> Terms { get; set; }
    }

    internal struct TermListItemModel
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }
    }
}
