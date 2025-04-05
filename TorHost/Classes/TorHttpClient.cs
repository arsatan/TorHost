using System.Net;
using System.Text.Json;

public class TorHttpClient
{
    private readonly HttpClient _httpClient;
    //public string TorAddress { get; set; } = "";
    private readonly ILogger<TorHttpClient> _logger;
    private readonly TorHostSettings _settings;

    public TorHttpClient(TorHostSettings settings, ILogger<TorHttpClient> logger)
    {
        _logger = logger;
        _settings = settings;
        var handler = new HttpClientHandler
        {
            Proxy = new WebProxy("socks5://127.0.0.1:9050"),
            UseProxy = true
        };
        _httpClient = new HttpClient(handler);
        _httpClient.Timeout = TimeSpan.FromMinutes(10);
    }
    public async Task<string> SendDataAndWaitForResponseAsync(object data)
    {
        if (_settings.CommandServerUrl == string.Empty)
            return "CommandServerUrl not defined";
        if (!_settings.CommandServerUrl.ToLower().Contains("http:"))
            _settings.CommandServerUrl = @"http://" + _settings.CommandServerUrl;
        var jsonContent = JsonContent.Create(data);
        var response = await _httpClient.PostAsync($"{_settings.CommandServerUrl}/api/tor/process", jsonContent);
        response.EnsureSuccessStatusCode();
        var responseContent = await response.Content.ReadAsStringAsync();
        return responseContent;
    }
    public void Start()
    {
    }
    public void Start(string torAddress, string data)
    {
        Send(new PostDataModel
        {
            Sender = torAddress,
            Data = data
        });
    }
    public void Send(PostDataModel postDataModel)
    {
        Task.Run(async () =>
        {
            int maxRetries = 1000;
            int currentAttempt = 0;
            TimeSpan delayBetweenRetries = TimeSpan.FromSeconds(10);
            bool success = false;
            while (!success && currentAttempt < maxRetries)
            {
                try
                {
                    currentAttempt++;
                    _logger.LogInformation($"[HTTPCLIENT] Attempt {currentAttempt}/{maxRetries}");
                    var response = await SendDataAndWaitForResponseAsync(postDataModel);
                    _logger.LogInformation($"[HTTPCLIENT] Success: {response}");
                    success = true;
                }
                catch (HttpRequestException httpEx)
                {
                    _logger.LogError($"[HTTPCLIENT] HTTP Error (Attempt {currentAttempt}): {httpEx.Message}");
                    await Task.Delay(delayBetweenRetries);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[HTTPCLIENT] Critical Error (Attempt {currentAttempt}): {ex.Message}");
                    break;
                }
            }
            if (!success)
            {
                _logger.LogError("[HTTPCLIENT] All attempts failed");
            }
        });

    }
    public void Stop()
    {
        _httpClient.Dispose();
    }
}