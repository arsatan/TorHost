using System.Diagnostics;
class KillProcess
{
    public static void KillAllProcesses(string processName)
    {
        foreach (var process in Process.GetProcessesByName(processName))
        {
            try
            {
                process.Kill();
                process.WaitForExit(3000);
            }
            catch { }
        }
    }
    public static void KillProcessById(int processId)
    {
        var process = Process.GetProcessById(processId);
        {
            try
            {
                process?.Kill();
                process?.WaitForExit(3000);
            }
            catch { }
        }
    }
}
