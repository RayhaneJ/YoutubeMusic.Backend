namespace MusicStreamServer.Models
{
    public class CircuitBreakerState
    {
        public bool IsOpen { get; set; }
        public DateTime OpenedAt { get; set; }
        public int ConsecutiveFailures { get; set; }
    }
}