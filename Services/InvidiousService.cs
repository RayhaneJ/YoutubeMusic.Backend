using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using MusicStreamServer.Models;

namespace MusicStreamServer.Services
{
    public class InvidiousService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<InvidiousService> _logger;
        private readonly IMemoryCache _cache;

        private readonly Dictionary<string, InstanceMetrics> _metrics = new();
        private readonly Dictionary<string, CircuitBreakerState> _circuitBreakers = new();

        private readonly JsonSerializerOptions _jsonOptions;

        private readonly string[] _instances = new[]
        {
            "https://invidious.f5.si",
            "https://invidious.privacyredirect.com",
            "https://inv.tux.pizza",
            "https://invidious.projectsegfau.lt",
            "https://yewtu.be",
            "https://vid.puffyan.us",
            "https://inv.nadeko.net",
            "https://invidious.nerdvpn.de",
            "https://inv.perditum.com",
            "https://invidious.privacydev.net"
        };

        public InvidiousService(
            IHttpClientFactory httpClientFactory,
            ILogger<InvidiousService> logger,
            IMemoryCache cache)
        {
            _httpClient = httpClientFactory.CreateClient("Invidious");
            _logger = logger;
            _cache = cache;

            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                NumberHandling = JsonNumberHandling.AllowReadingFromString
            };

            _logger.LogInformation("[Invidious] Service initialized with {Count} instances", _instances.Length);
        }


        private IEnumerable<string> GetSortedInstances()
        {
            return _instances
                .Where(IsInstanceAvailable)
                .OrderByDescending(CalculateInstanceScore)
                .ToList();
        }

        private double CalculateInstanceScore(string instance)
        {
            if (!_metrics.ContainsKey(instance) || _metrics[instance].TotalRequests == 0)
            {
                return 50.0; // Score neutre
            }

            var metrics = _metrics[instance];
            var successRate = metrics.SuccessfulRequests * 100.0 / metrics.TotalRequests;
            var responseScore = Math.Max(0, 100 - (metrics.AverageResponseTime / 10));

            return (successRate * 0.7) + (responseScore * 0.3);
        }

        private bool IsInstanceAvailable(string instance)
        {
            if (!_circuitBreakers.ContainsKey(instance))
            {
                return true;
            }

            var breaker = _circuitBreakers[instance];

            if (breaker.IsOpen)
            {
                if ((DateTime.UtcNow - breaker.OpenedAt).TotalMinutes >= 5)
                {
                    _logger.LogInformation("[Invidious] Circuit breaker reset for {Instance}", instance);
                    breaker.IsOpen = false;
                    breaker.ConsecutiveFailures = 0;
                    return true;
                }

                return false;
            }

            return true;
        }

        private void RecordSuccess(string instance)
        {
            if (_circuitBreakers.ContainsKey(instance))
            {
                _circuitBreakers[instance].ConsecutiveFailures = 0;
            }
        }

        private void RecordFailure(string instance)
        {
            if (!_circuitBreakers.ContainsKey(instance))
            {
                _circuitBreakers[instance] = new CircuitBreakerState();
            }

            var breaker = _circuitBreakers[instance];
            breaker.ConsecutiveFailures++;

            if (breaker.ConsecutiveFailures >= 3)
            {
                breaker.IsOpen = true;
                breaker.OpenedAt = DateTime.UtcNow;
                _logger.LogWarning("[Invidious] ⚠️ Circuit breaker opened for {Instance}", instance);
            }
        }

        private void UpdateMetrics(string instance, bool success, double responseTime)
        {
            if (!_metrics.ContainsKey(instance))
            {
                _metrics[instance] = new InstanceMetrics();
            }

            var metrics = _metrics[instance];
            metrics.TotalRequests++;

            if (success)
            {
                metrics.SuccessfulRequests++;
                metrics.AverageResponseTime =
                    (metrics.AverageResponseTime * (metrics.SuccessfulRequests - 1) + responseTime)
                    / metrics.SuccessfulRequests;
            }
            else
            {
                metrics.FailedRequests++;
            }

            metrics.LastUpdated = DateTime.UtcNow;
        }

        public async Task<string?> GetStreamUrlAsync(string videoId)
        {
            var cacheKey = $"inv_stream_{videoId}";

            if (_cache.TryGetValue(cacheKey, out string? cachedUrl))
            {
                _logger.LogInformation("[Invidious] Cache hit for {VideoId}", videoId);
                return cachedUrl;
            }

            var sortedInstances = GetSortedInstances().ToList();

            if (!sortedInstances.Any())
            {
                _logger.LogError("[Invidious] No available instances");
                return null;
            }

            foreach (var instance in sortedInstances)
            {
                var startTime = DateTime.UtcNow;

                try
                {
                    _logger.LogInformation("[Invidious] Trying {Instance} for {VideoId}", instance, videoId);

                    var url = $"{instance}/api/v1/videos/{videoId}";

                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    var response = await _httpClient.GetAsync(url, cts.Token);

                    var responseTime = (DateTime.UtcNow - startTime).TotalMilliseconds;

                    if (!response.IsSuccessStatusCode)
                    {
                        UpdateMetrics(instance, false, responseTime);
                        RecordFailure(instance);

                        _logger.LogWarning("[Invidious] {Instance} returned {StatusCode}", instance, response.StatusCode);
                        continue;
                    }

                    var json = await response.Content.ReadAsStringAsync(cts.Token);
                    var video = JsonSerializer.Deserialize<InvidiousVideo>(json, _jsonOptions);

                    if (video?.AdaptiveFormats == null || !video.AdaptiveFormats.Any())
                    {
                        UpdateMetrics(instance, false, responseTime);
                        RecordFailure(instance);

                        _logger.LogWarning("[Invidious] No formats found on {Instance}", instance);
                        continue;
                    }

                    var audioFormat = video.AdaptiveFormats
                        .Where(f => f.Type?.Contains("audio") == true)
                        .OrderByDescending(f => f.Bitrate ?? 0)
                        .FirstOrDefault();

                    if (audioFormat?.Url == null)
                    {
                        UpdateMetrics(instance, false, responseTime);
                        RecordFailure(instance);

                        _logger.LogWarning("[Invidious] No audio URL on {Instance}", instance);
                        continue;
                    }

                    UpdateMetrics(instance, true, responseTime);
                    RecordSuccess(instance);

                    var streamUrl = audioFormat.Url;
                    _cache.Set(cacheKey, streamUrl, TimeSpan.FromHours(5));

                    _logger.LogInformation("[Invidious] ✅ Success via {Instance} ({ResponseTime:F0}ms)", instance, responseTime);
                    return streamUrl;
                }
                catch (TaskCanceledException)
                {
                    var responseTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
                    UpdateMetrics(instance, false, responseTime);
                    RecordFailure(instance);

                    _logger.LogWarning("[Invidious] Timeout on {Instance}", instance);
                }
                catch (HttpRequestException ex)
                {
                    var responseTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
                    UpdateMetrics(instance, false, responseTime);
                    RecordFailure(instance);

                    _logger.LogWarning("[Invidious] HTTP error on {Instance}: {Message}", instance, ex.Message);
                }
                catch (JsonException ex)
                {
                    var responseTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
                    UpdateMetrics(instance, false, responseTime);
                    RecordFailure(instance);

                    _logger.LogWarning("[Invidious] JSON error on {Instance}: {Message}", instance, ex.Message);
                }
                catch (Exception ex)
                {
                    var responseTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
                    UpdateMetrics(instance, false, responseTime);
                    RecordFailure(instance);

                    _logger.LogWarning(ex, "[Invidious] Unexpected error on {Instance}", instance);
                }
            }

            _logger.LogError("[Invidious] ❌ All instances failed for {VideoId}", videoId);
            return null;
        }

        public async Task<List<Track>> SearchAsync(string query, int limit = 20)
        {
            var sortedInstances = GetSortedInstances().ToList();

            if (!sortedInstances.Any())
            {
                _logger.LogError("[Invidious] No available instances for search");
                return new List<Track>();
            }

            foreach (var instance in sortedInstances)
            {
                var startTime = DateTime.UtcNow;

                try
                {
                    _logger.LogInformation("[Invidious] Searching via {Instance}: {Query}", instance, query);

                    var url = $"{instance}/api/v1/search?q={Uri.EscapeDataString(query)}&type=video&sort_by=relevance";

                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    var response = await _httpClient.GetAsync(url, cts.Token);

                    var responseTime = (DateTime.UtcNow - startTime).TotalMilliseconds;

                    if (!response.IsSuccessStatusCode)
                    {
                        UpdateMetrics(instance, false, responseTime);
                        RecordFailure(instance);

                        _logger.LogWarning("[Invidious] Search failed on {Instance}: {StatusCode}", instance, response.StatusCode);
                        continue;
                    }

                    var json = await response.Content.ReadAsStringAsync(cts.Token);
                    var videos = JsonSerializer.Deserialize<List<InvidiousSearchResult>>(json, _jsonOptions);

                    if (videos == null || !videos.Any())
                    {
                        UpdateMetrics(instance, false, responseTime);
                        RecordFailure(instance);
                        continue;
                    }

                    UpdateMetrics(instance, true, responseTime);
                    RecordSuccess(instance);

                    var results = videos
                        .Take(limit)
                        .Select(v => new Track
                        {
                            Id = v.VideoId ?? "",
                            Title = v.Title ?? "",
                            Artist = v.Author ?? "",
                            Duration = v.LengthSeconds ?? 0,
                            ThumbnailUrl = v.VideoThumbnails?.FirstOrDefault()?.Url ?? ""
                        })
                        .ToList();

                    _logger.LogInformation("[Invidious] ✅ Found {Count} results via {Instance} ({ResponseTime:F0}ms)",
                        results.Count, instance, responseTime);
                    return results;
                }
                catch (TaskCanceledException)
                {
                    var responseTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
                    UpdateMetrics(instance, false, responseTime);
                    RecordFailure(instance);

                    _logger.LogWarning("[Invidious] Search timeout on {Instance}", instance);
                }
                catch (HttpRequestException ex)
                {
                    var responseTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
                    UpdateMetrics(instance, false, responseTime);
                    RecordFailure(instance);

                    _logger.LogWarning("[Invidious] HTTP error on {Instance}: {Message}", instance, ex.Message);
                }
                catch (JsonException ex)
                {
                    var responseTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
                    UpdateMetrics(instance, false, responseTime);
                    RecordFailure(instance);

                    _logger.LogWarning("[Invidious] JSON error on {Instance}: {Message}", instance, ex.Message);
                }
                catch (Exception ex)
                {
                    var responseTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
                    UpdateMetrics(instance, false, responseTime);
                    RecordFailure(instance);

                    _logger.LogWarning(ex, "[Invidious] Search error on {Instance}", instance);
                }
            }

            _logger.LogError("[Invidious] ❌ All instances failed for search: {Query}", query);
            return new List<Track>();
        }

        public Dictionary<string, object> GetMetrics()
        {
            return _instances.ToDictionary(
                instance => instance,
                instance => (object)new
                {
                    total = _metrics.ContainsKey(instance) ? _metrics[instance].TotalRequests : 0,
                    success = _metrics.ContainsKey(instance) ? _metrics[instance].SuccessfulRequests : 0,
                    failed = _metrics.ContainsKey(instance) ? _metrics[instance].FailedRequests : 0,
                    successRate = _metrics.ContainsKey(instance) && _metrics[instance].TotalRequests > 0
                        ? _metrics[instance].SuccessfulRequests * 100.0 / _metrics[instance].TotalRequests
                        : 0,
                    avgResponseTime = _metrics.ContainsKey(instance) ? _metrics[instance].AverageResponseTime : 0,
                    score = CalculateInstanceScore(instance),
                    circuitBreakerOpen = _circuitBreakers.ContainsKey(instance) && _circuitBreakers[instance].IsOpen,
                    lastUpdated = _metrics.ContainsKey(instance) ? _metrics[instance].LastUpdated : DateTime.MinValue
                }
            );
        }
    }
}