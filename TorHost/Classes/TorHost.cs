using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Net.Sockets;
using System.ServiceProcess;
using System.Text;
using System.Timers;
using Timer = System.Timers.Timer;

public partial class TorHost : ServiceBase
{
    //private bool _isTorInitialized = false;
    private Process _torProcess;
    private string _torAddress = string.Empty;
    private readonly string _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
    private string _torPath = AppDomain.CurrentDomain.BaseDirectory + @"\Tor\";
    private Timer _timer;
    private readonly WebServer _webServer;
    private readonly SshServer _sshServer;
    private readonly ILogger<TorHost> _logger;
    private readonly TorHostSettings _settings;
    private IWebHost _webHost;
    private readonly TorHttpClient _httpClient;

    public TorHost(TorHostSettings settings, WebServer webServer, SshServer sshServer, TorHttpClient httpClient, ILogger<TorHost> logger)
    {
        _settings = settings;
        _webServer = webServer;
        _sshServer = sshServer;
        _httpClient = httpClient;
        _logger = logger;
        _timer = new Timer(TimeSpan.FromHours(12));
        _timer.Elapsed += CheckRestartDate;
    }
    private readonly ManualResetEvent _serviceReadyEvent = new ManualResetEvent(false);
    public void Start()
    {
        UpdateRestartDate();
        OnStart(null);
    }
    public void Stop()
    {
        OnStop();
    }
    public void StartWebServer()
    {
        try
        {
            _logger.LogInformation($"Starting Web Server on port {_settings.WebServer.WebServerPort}...");
            _webServer.Start();
            _logger.LogInformation("Web server started");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Web Server startup failed");
        }
    }
    public void StartSshServer()
    {
        try
        {
            _logger.LogInformation($"Starting SSH Server on port {_settings.SshServer.SshServerPort}...");
            _sshServer.Start();
            var timer = new Timer(5000);
            timer.Elapsed += (sender, e) => _sshServer.CheckAndRestart();
            timer.Start();
            _logger.LogInformation("Ssh server started");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ssh server startup failed");
        }
    }
    public async Task StartHttpClient()
    {
        try
        {
            _logger.LogInformation("Starting http client...");
            ////_httpClient.TorAddress = _torAddress;
            _httpClient.Start(DataReader.GetTorAddress(_torPath), DataReader.GetData(_torPath, _sshServer.SshServerPath));
            _logger.LogInformation("Http client started");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Http client startup failed");
        }
    }
    public void StopWebServer()
    {
        _webServer.Stop();
    }
    public void StopSshServer()
    {
        _sshServer.Stop();
    }
    public void StopHttpClient()
    {
        _httpClient.Stop();
    }
    protected override void OnStart(string[] args)
    {
        _timer.Start();
        Task.Run(async () =>
        {
            _logger.LogInformation($"[ONSTART] Executing...");
            await Task.Delay(TimeSpan.FromSeconds(30));
            ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                StartTor();
                if (_settings.WebServer.Enabled)
                {
                    StartWebServer();
                }
                if (_settings.SshServer.Enabled)
                {
                    StartSshServer();
                }

            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Service startup failed");
                Stop();
            }
            Task.Run(async () =>
            {
                await StartHttpClient();
            });
            _serviceReadyEvent.Set();
        });
        });
    }
    protected override void OnStop()
    {

        _timer.Stop();
        _webHost?.Dispose();
        StopWebServer();
        StopSshServer();
        StopHttpClient();
        StopTor();
    }
    public TorHost(ILogger<TorHost> logger)
    {
        _logger = logger;
    }
    public TorHost()
    {
        ServiceName = "TorHost";
        CanStop = true;
        CanPauseAndContinue = false;
    }
    public void StartTor()
    {
        try
        {
            _logger.LogInformation($"Starting Tor initialization. Working dir: {_torPath}");

            // Проверка и создание директории
            if (!Directory.Exists(_torPath))
            {
                _logger.LogWarning($"Creating Tor directory: {_torPath}");
                Directory.CreateDirectory(_torPath);
            }
            // Проверка существования tor.exe
            var torExePath = Path.Combine(_torPath, "tor.exe");
            if (!File.Exists(torExePath))
            {
                _logger.LogError($"Tor executable not found at: {torExePath}");
                return;
            }
            GenerateTorConfig();
            _torProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = torExePath,
                    Arguments = "-f torrc",
                    WorkingDirectory = _torPath,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            // Захват вывода Tor
            _torProcess.OutputDataReceived += (sender, args)
                => _logger.LogInformation($"[TOR] {args.Data}");
            _torProcess.ErrorDataReceived += (sender, args)
                => _logger.LogError($"[TOR] {args.Data}");

            _logger.LogInformation("Starting Tor process...");
            if (!_torProcess.Start())
            {
                _logger.LogCritical("Failed to start Tor process");
                return;
            }
            _torProcess.BeginOutputReadLine();
            _torProcess.BeginErrorReadLine();
            _torAddress = DataReader.GetTorAddress(_torPath);//File.ReadAllText($"{TorPath}\\hidden_service\\hostname");
            _logger.LogInformation($"Tor started with PID: {_torProcess.Id}");

            //// Добавляем обработчик вывода для определения готовности
            //torProcess.OutputDataReceived += (sender, args) =>
            //{
            //    if (args.Data?.Contains("Bootstrapped 100%") == true)
            //    {
            //        _isTorInitialized = true;
            //        _logger.LogInformation("Tor fully initialized");
            //    }
            //    _logger.LogInformation($"[TOR] {args.Data}");
            //};
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Tor initialization failed");
        }
    }

    public void GenerateTorConfig()
    {
        try
        {
            // Чтение шаблона
            var templatePath = Path.Combine(_torPath, "torrc.template");
            if (!File.Exists(templatePath))
            {
                _logger.LogError($"Template file not found: {templatePath}");
                return;
            }

            var configContent = "#### DO NOT EDIT THIS FILE. You can edit torc.template.\r\n" + File.ReadAllText(templatePath);

            // Замена плейсхолдеров для WebHost
            if (_settings.WebHost.Enabled)
            {
                configContent = configContent
                    .Replace("{HiddenServiceWebHostPort}", _settings.WebHost.HiddenServiceWebHostPort.ToString())
                    .Replace("{WebHostPort}", _settings.WebHost.WebHostPort.ToString());
            }

            // Замена плейсхолдеров для SSH
            if (_settings.SshServer.Enabled)
            {
                configContent = configContent
                    .Replace("{HiddenServiceSshServerPort}", _settings.SshServer.HiddenServiceSshServerPort.ToString())
                    .Replace("{SshServerPort}", _settings.SshServer.SshServerPort.ToString());
            }

            // Замена плейсхолдеров для WebServer
            if (_settings.WebServer.Enabled)
            {
                configContent = configContent
                    .Replace("{HiddenServiceWebServerPort}", _settings.WebServer.HiddenServiceWebServerPort.ToString())
                    .Replace("{WebServerPort}", _settings.WebServer.WebServerPort.ToString());
            }

            // Сохранение конфига
            var configPath = Path.Combine(_torPath, "torrc");
            File.WriteAllText(configPath, configContent);

            _logger.LogInformation($"Tor config generated: {configPath}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tor config generation failed");
        }
    }

    //public bool IsTorReady()
    //{
    //    return _isTorInitialized;
    //}
    public void StopTor()
    {
        try
        {
            if (_torProcess != null && !_torProcess.HasExited)
            {
                // Способ 1: Через Control Port (рекомендуется)
                //SendControlSignal("SIGNAL SHUTDOWN");

                // Способ 2: Принудительное завершение
                
                //if (!_torProcess.WaitForExit(5000))
                //{
                    _torProcess.Kill();
                    _logger.LogWarning("Tor killed forcefully");
                //}

                _torProcess.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tor stop error");
            KillProcess.KillProcessById(_torProcess.Id);
            //KillProcess.KillAllProcesses("tor");
        }
    }
    private void SendControlSignal(string signal)
    {
        try
        {
            using var client = new TcpClient("127.0.0.1", 9051);
            using var stream = client.GetStream();
            var message = $"AUTHENTICATE\r\n{signal}\r\nQUIT\r\n";
            var data = Encoding.ASCII.GetBytes(message);
            stream.Write(data, 0, data.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Control port command failed");
        }
    }

    private void CheckRestartDate(object sender, ElapsedEventArgs e)
    {
        try
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            var nextRestartDateStr = config["RestartSettings:NextRestartDate"];
            if (DateTime.TryParse(nextRestartDateStr, out DateTime nextRestartDate))
            {
                if (DateTime.Now >= nextRestartDate)
                {
                    UpdateRestartDate(); // Обновляем дату перед перезапуском
                    RestartService();
                }
            }
        }
        catch (Exception ex)
        {
            // Логирование ошибки
            File.WriteAllText("service_log.txt", $"{DateTime.Now}: {ex.Message}\n");
        }
    }

    private void UpdateRestartDate()
    {
        try
        {
            var json = JObject.Parse(File.ReadAllText(_configPath));
            json["RestartSettings"]["NextRestartDate"] = DateTime.Now.AddDays(1).ToString("yyyy-MM-dd");
            File.WriteAllText(_configPath, json.ToString(Formatting.Indented));
        }
        catch (Exception ex)
        {
            File.WriteAllText("config_error.txt", $"{DateTime.Now}: {ex.Message}\n");
        }
    }

    private void RestartService()
    {
        new System.Threading.Thread(() =>
        {
            using (var sc = new ServiceController("YourServiceName"))
            {
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped);
                sc.Start();
            }
        }).Start();
    }
}