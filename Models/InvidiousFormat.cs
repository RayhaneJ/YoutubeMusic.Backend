using System.Text.Json.Serialization;

namespace MusicStreamServer.Models
{
    public class InvidiousFormat
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("bitrate")]
        public int? Bitrate { get; set; }

        [JsonPropertyName("container")]
        public string? Container { get; set; }
    }
}