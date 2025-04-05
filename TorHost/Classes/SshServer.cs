/// TODO: Внимание! В интерактивном режиме SSH-сервер не запускается от LocalSystem
/// Поэтому коннекта не будет!

using System.Diagnostics;

public class SshServer : IDisposable
{
    private readonly ILogger<SshServer> _logger;
    private readonly SshServerSettings _settings;
    private Process _sshProcess;
    public string SshServerPath = AppDomain.CurrentDomain.BaseDirectory + @"/OpenSSH/";
    private string UserProfilePath = string.Empty;
    public SshServer(TorHostSettings settings, ILogger<SshServer> logger)
    {
        _settings = settings.SshServer;
        _logger = logger;
    }

    private bool IsSshdRunning()
    {
        return Process.GetProcessesByName("sshd").Length > 0;
    }
    public void Start()
    {
        UserProfilePath = UserProfileHelper.GetActiveUserProfilePath();
        if (UserProfilePath == string.Empty)
        {
            _logger.LogError("UserProfilePath not yet set.");
            return;
        }
        ConfigureFirewall.Configure("netsh advfirewall firewall add rule name=\"SSH\" dir=in action=allow protocol=TCP localport=" + _settings.SshServerPort.ToString());
        _logger.LogInformation($"SSH user directory: {UserProfilePath}");
        ProcessStartInfo StartInfo = new ProcessStartInfo
        {
            FileName = SshServerPath + @"ssh-keygen.exe",
            Arguments = $"-A",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        StartInfo.EnvironmentVariables.Clear(); 
        StartInfo.EnvironmentVariables.Add("ProgramData", SshServerPath);
        new Process()
        {
            StartInfo = StartInfo
        }.Start();
        StartInfo = new ProcessStartInfo
        {
            FileName = SshServerPath + @"ssh-keygen.exe",
            Arguments = $"-t rsa -b 4096 -f {UserProfilePath}/id_rsa -q -N \"\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        StartInfo.EnvironmentVariables.Clear();
        StartInfo.EnvironmentVariables.Add("ProgramData", SshServerPath);
        new Process()
        {
            StartInfo = StartInfo
        }.Start();

        try
        {
            _sshProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = SshServerPath + @"sshd.exe",
                    Arguments = $"-f {SshServerPath}etc/sshd_config -d -E {SshServerPath}var/log/debug.log -p {_settings.SshServerPort}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };
            _sshProcess.Start();
            _logger.LogInformation($"SSH server started on port {_settings.SshServerPort}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SSH server failed to start");
        }
    }

    public void Stop()
    {
        KillProcess.KillProcessById(_sshProcess.Id);
        KillProcess.KillAllProcesses("ssh-keygen");
    }

    public void CheckAndRestart()
    {
        if (!IsSshdRunning())
        {
            _logger.LogWarning("Restarting sshd...");
            KillProcess.KillAllProcesses("ssh-keygen");
            Start();
        }
    }

    public void Dispose()
    {
        KillProcess.KillAllProcesses("ssh-keygen");
        _sshProcess?.Kill();
        _sshProcess?.Dispose();
    }
}