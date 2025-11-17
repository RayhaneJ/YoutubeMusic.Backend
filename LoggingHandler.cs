namespace MusicStreamServer
{
    public class LoggingHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Console.WriteLine($"Request: {request.Method} {request.RequestUri}");

            var response = await base.SendAsync(request, cancellationToken);

            Console.WriteLine($"Response Status: {response.StatusCode}");
            var content = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"Response Content: {content.Substring(0, Math.Min(content.Length, 500))}"); // Limit for large content

            return response;
        }
    }

}
