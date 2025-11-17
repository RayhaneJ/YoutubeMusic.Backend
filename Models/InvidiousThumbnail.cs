using System.Text.Json.Serialization;

namespace MusicStreamServer.Models
{
    public class InvidiousThumbnail
    {
        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("quality")]
        public string? Quality { get; set; }
    }
}