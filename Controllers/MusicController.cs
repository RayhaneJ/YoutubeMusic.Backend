using Microsoft.AspNetCore.Mvc;
using MusicStreamServer.Models;
using MusicStreamServer.Services;

namespace MusicStreamServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MusicController : ControllerBase
    {
        private readonly InvidiousService _invidiousService;
        private readonly ILogger<MusicController> _logger;

        public MusicController(
            InvidiousService invidiousService,
            ILogger<MusicController> logger)
        {
            _invidiousService = invidiousService;
            _logger = logger;
        }

        [HttpPost("search")]
        public async Task<IActionResult> Search([FromBody] SearchRequest request)
        {
            if (string.IsNullOrEmpty(request.Query))
            {
                return BadRequest(new { error = "Query required" });
            }

            _logger.LogInformation($"Search request: {request.Query}");

            var items = await _invidiousService.SearchAsync(request.Query, request.MaxResults);

            return Ok(new SearchResponse
            {
                Tracks = items,
                Success = items.Any(),
                ErrorMessage = items.Any() ? null : "No results found"
            });
        }

        [HttpGet("stream/{videoId}")]
        public async Task<IActionResult> GetStreamUrl(string videoId)
        {
            if (string.IsNullOrEmpty(videoId))
            {
                return BadRequest(new { error = "Video ID required" });
            }

            _logger.LogInformation($"Stream request: {videoId}");

            var streamUrl = await _invidiousService.GetStreamUrlAsync(videoId);

            if (streamUrl == null)
            {
                return NotFound(new
                {
                    success = false,
                    error = "Stream not found"
                });
            }

            return Ok(new StreamResponse
            {
                StreamUrl = streamUrl,
                Success = true
            });
        }
    }
}