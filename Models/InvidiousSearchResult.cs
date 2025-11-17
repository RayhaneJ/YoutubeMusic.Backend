using System.Text.Json.Serialization;

namespace MusicStreamServer.Models
{
    public class InvidiousSearchResult
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("videoId")]
        public string? VideoId { get; set; }

        [JsonPropertyName("author")]
        public string? Author { get; set; }

        [JsonPropertyName("lengthSeconds")]
        public long? LengthSeconds { get; set; }

        [JsonPropertyName("videoThumbnails")]
        public List<InvidiousThumbnail>? VideoThumbnails { get; set; }
    }
}