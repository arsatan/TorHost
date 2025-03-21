public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly TorHost _torServiceHost;

    public Worker(
   ILogger<Worker> logger,
   TorHost torServiceHost)
    {
        _logger = logger;
        _torServiceHost = torServiceHost;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting services");
        _torServiceHost.Start();
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping services");
        _torServiceHost.Stop();
        await base.StopAsync(cancellationToken);
        _logger.LogInformation("Service stopped gracefully");
    }

    private async Task CheckServerAvailabilityWithRetry(int maxRetries, TimeSpan delay)
    {
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                var response = await new System.Net.Http.HttpClient().GetAsync($"http://127.0.0.1:8080/");
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation($"Server ready! Status: {response.StatusCode}");
                    return;
                }
            }
            catch { }

            _logger.LogDebug($"Retry {i + 1}/{maxRetries}");
            await Task.Delay(delay);
        }
        _logger.LogError("Server availability check failed");
    }
}
