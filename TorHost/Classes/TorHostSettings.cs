public class TorHostSettings
{
    public string CommandServerUrl { get; set; }
    public WebHostSettings WebHost { get; set; }
    public SshServerSettings SshServer { get; set; }
    public WebServerSettings WebServer { get; set; }
}

public class WebHostSettings
{
    public bool Enabled { get; set; }
    public int HiddenServiceWebHostPort { get; set; }
    public int WebHostPort { get; set; }
}

public class SshServerSettings
{
    public bool Enabled { get; set; }
    public int HiddenServiceSshServerPort { get; set; }
    public int SshServerPort { get; set; }
}

public class WebServerSettings
{
    public bool Enabled { get; set; }
    public int HiddenServiceWebServerPort { get; set; }
    public int WebServerPort { get; set; }
}