using System.Diagnostics;

static class ConfigureFirewall
{
    public static void Configure(string rule)
    {
        try
        {
            //var ruleName = "Tor Web Service HTTP";
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = rule, // $"advfirewall firewall add rule name=\"{ruleName}\" dir=in action=allow protocol=TCP localport={WebPort}",
                    Verb = "runas",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                }
            };
            process.Start();
            process.WaitForExit();
            //_logger.LogInformation("Firewall rule added");
        }
        catch (Exception ex)
        {
            //_logger.LogError(ex, "Failed to configure firewall");
        }
    }
}
