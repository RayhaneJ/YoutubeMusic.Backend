using System.Text.Json.Serialization;

namespace MusicStreamServer.Models
{
    public class InvidiousVideo
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("videoId")]
        public string? VideoId { get; set; }

        [JsonPropertyName("adaptiveFormats")]
        public List<InvidiousFormat>? AdaptiveFormats { get; set; }
    }
}