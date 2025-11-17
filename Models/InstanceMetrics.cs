namespace MusicStreamServer.Services
{
    public class InstanceMetrics
    {
        public int TotalRequests { get; set; }
        public int SuccessfulRequests { get; set; }
        public int FailedRequests { get; set; }
        public double AverageResponseTime { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}