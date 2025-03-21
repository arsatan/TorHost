using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;

public class WebServer
{
    private readonly ILogger<WebServer> _logger;
    private readonly WebServerSettings _settings;
    private HttpListener listener;
    private Thread serverThread;

    public WebServer(TorHostSettings settings, ILogger<WebServer> logger)
    {
        _settings = settings.WebServer;
        _logger = logger;
    }
    public void Start()
    {
        ConfigureFirewall.Configure("advfirewall firewall add rule name=\"WebServer\" dir=in action=allow protocol=TCP localport=" + _settings.WebServerPort.ToString()); ;
        try
        {
            if (IsPortInUse(_settings.WebServerPort))
            {
                _logger.LogError($"Port {_settings.WebServerPort} is already in use");
                return;
            }
            _logger.LogInformation("Initializing web server...");
            listener = new HttpListener();
            listener.Prefixes.Add($"http://+:{_settings.WebServerPort}/");
            try
            {
                listener.Start();
                _logger.LogInformation("HTTP Listener started");
            }
            catch (HttpListenerException ex)
            {
                _logger.LogError($"HTTP listener error: {ex.Message}");
            }

            serverThread = new Thread(() =>
            {
                try
                {
                    while (listener.IsListening)
                    {
                        var context = listener.GetContext();
                        _logger.LogInformation($"Request from: {context.Request.RemoteEndPoint}");
                        ProcessRequest(context);
                    }

                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Web server thread crashed");
                }
                finally
                {
                    _logger.LogInformation("Web server thread stopped");
                }
            })
            {
                IsBackground = true
            };
            serverThread.Start();
            Thread.Sleep(500);
            _logger.LogInformation($"Server status: {listener.IsListening}");
            Task.Delay(1000).ContinueWith(_ => CheckServerAvailability());
            _logger.LogInformation($"Web server started on port {_settings.WebServerPort}");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Web server failed to start");
            throw;
        }
    }
    public void Stop()
    {
        if (listener != null && listener.IsListening)
        {
            listener.Stop();
        }
        serverThread?.Join(3000);
    }
    private void ProcessRequest(object state)
    {
        var context = (HttpListenerContext)state;
        try
        {
            _logger.LogDebug($"Request from: {context.Request.RemoteEndPoint}");

            var response = $"<h1>Tor Web Service</h1><p>{DateTime.UtcNow}</p>";
            var buffer = Encoding.UTF8.GetBytes(response);
            context.Response.StatusCode = 200; // Явно устанавливаем статус
            context.Response.ContentType = "text/html";
            context.Response.ContentLength64 = buffer.Length;
            context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            context.Response.Close();

            _logger.LogInformation($"Server request: {context.Request.Url}");
        }
        catch (Exception ex)
        {
            context.Response.StatusCode = 503; // Service Unavailable
            _logger.LogError(ex, "Request processing failed");
        }
    }
    private bool IsPortInUse(int port)
    {
        var ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
        var tcpConnInfoArray = ipGlobalProperties.GetActiveTcpListeners();
        return tcpConnInfoArray.Any(endpoint => endpoint.Port == port);
    }
    private async Task CheckServerAvailability()
    {
        using var client = new System.Net.Http.HttpClient();
        try
        {
            var response = await client.GetAsync($"http://127.0.0.1:{_settings.WebServerPort}/");
            _logger.LogInformation($"Web Server response: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Web Server check failed");
        }
    }
    private void ReserveUrl()
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = $"http add urlacl url=http://+:{_settings.WebServerPort}/ user=\"Все\"",
                    Verb = "runas",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                }
            };
            process.Start();
            process.WaitForExit();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reserve URL");
        }
    }
}
