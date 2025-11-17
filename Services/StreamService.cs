using YoutubeExplode;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;
using YoutubeExplode.Search;
using MusicStreamServer.Models;
using System.Net;
using YoutubeExplode.Common;

namespace MusicStreamServer.Services
{
    public class StreamService
    {
        private readonly YoutubeClient _youtube;
        private readonly ILogger<StreamService> _logger;
        private readonly HttpClient _httpClient;

        // Liste de proxies (si vous en avez)
        private readonly List<string> _proxies = new()
        {
            // Ajoutez vos proxies ici si nécessaire
            // "http://proxy1.com:8080",
            // "http://proxy2.com:8080",
        };

        private int _currentProxyIndex = 0;

        public StreamService(ILogger<StreamService> logger, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _httpClient = httpClientFactory.CreateClient("Youtube");

            // Configuration du client YouTube avec proxy si disponible
            if (_proxies.Any())
            {
                var proxy = new WebProxy(_proxies[_currentProxyIndex]);
                var httpClientHandler = new HttpClientHandler
                {
                    Proxy = proxy,
                    UseProxy = true
                };
                var httpClient = new HttpClient(httpClientHandler);
                _youtube = new YoutubeClient(httpClient);
            }
            else
            {
                _youtube = new YoutubeClient(_httpClient);
            }
        }

        public async Task<SearchResponse> SearchTracksAsync(string query, int maxResults = 20)
        {
            try
            {
                _logger.LogInformation($"Searching for: {query}");

                var searchResults = await _youtube.Search.GetVideosAsync(query).CollectAsync(maxResults);

                var tracks = searchResults.Select(video => new Track
                {
                    Id = video.Id.Value,
                    Title = video.Title,
                    Artist = video.Author.ChannelTitle,
                    Duration = (long)video.Duration?.TotalSeconds,
                    ThumbnailUrl = video.Thumbnails.GetWithHighestResolution()?.Url ?? "",
                    VideoUrl = video.Url
                }).ToList();

                _logger.LogInformation($"Found {tracks.Count} results");

                return new SearchResponse
                {
                    Tracks = tracks,
                    Success = true
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Search failed");
                return new SearchResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<StreamResponse> GetStreamUrlAsync(string videoId)
        {
            try
            {
                _logger.LogInformation($"Getting stream URL for: {videoId}");

                // Récupérer le manifest des streams
                var streamManifest = await _youtube.Videos.Streams.GetManifestAsync(videoId);

                // Obtenir le meilleur stream audio uniquement
                var audioStreamInfo = streamManifest
                    .GetAudioOnlyStreams()
                    .OrderByDescending(s => s.Bitrate)
                    .FirstOrDefault();

                if (audioStreamInfo == null)
                {
                    throw new Exception("No audio stream found");
                }

                _logger.LogInformation($"Stream URL obtained: {audioStreamInfo.Url}");

                return new StreamResponse
                {
                    StreamUrl = audioStreamInfo.Url,
                    Title = "video.Title",
                    Duration = (long)99,
                    Success = true
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting stream URL");

                // Rotation du proxy en cas d'erreur
                RotateProxy();

                return new StreamResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<byte[]> DownloadAudioAsync(string videoId)
        {
            try
            {
                var streamManifest = await _youtube.Videos.Streams.GetManifestAsync(videoId);
                var audioStreamInfo = streamManifest
                    .GetAudioOnlyStreams()
                    .OrderByDescending(s => s.Bitrate)
                    .FirstOrDefault();

                if (audioStreamInfo == null)
                {
                    throw new Exception("No audio stream found");
                }

                using var memoryStream = new MemoryStream();
                await _youtube.Videos.Streams.CopyToAsync(audioStreamInfo, memoryStream);
                return memoryStream.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading audio");
                throw;
            }
        }

        private void RotateProxy()
        {
            if (!_proxies.Any()) return;

            _currentProxyIndex = (_currentProxyIndex + 1) % _proxies.Count;
            _logger.LogInformation($"Rotating to proxy: {_proxies[_currentProxyIndex]}");
        }
    }
}