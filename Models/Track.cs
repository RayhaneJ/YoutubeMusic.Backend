namespace MusicStreamServer.Models
{
    public class Track
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public long Duration { get; set; }
        public string ThumbnailUrl { get; set; } = string.Empty;
        public string VideoUrl { get; set; } = string.Empty;
    }

    public class SearchRequest
    {
        public string Query { get; set; } = string.Empty;
        public int MaxResults { get; set; } = 20;
    }

    public class SearchResponse
    {
        public List<Track> Tracks { get; set; } = new();
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class StreamRequest
    {
        public string VideoId { get; set; } = string.Empty;
    }

    public class StreamResponse
    {
        public string StreamUrl { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public long Duration { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }
}