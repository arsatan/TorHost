using System.Collections;
using System.ComponentModel;
using System.Configuration.Install;
using System.Diagnostics;
using System.ServiceProcess;

public class TorHostInstaller
{
    public static bool IsServiceInstalled(string serviceName)
    {
        return ServiceController.GetServices()
            .Any(service => service.ServiceName.Equals(serviceName, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsServiceRunning(string serviceName)
    {
        using (var controller = new ServiceController(serviceName))
        {
            try {

                return controller.Status == ServiceControllerStatus.Running;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    public static void StartService(string serviceName)
    {
        if (!IsServiceInstalled(serviceName))
            return;
        using (var controller = new ServiceController(serviceName))
        {
            try
            {
                if (controller.Status != ServiceControllerStatus.Running)
                {
                    controller.Start();
                    controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
                }
            }
            catch (InvalidOperationException)
            {
                return;
            }
        }
    }
    public static void StopService(string serviceName)
    {
        if (!IsServiceInstalled(serviceName))
            return;
        using (var controller = new ServiceController(serviceName))
        {
            if (controller.Status == ServiceControllerStatus.Running)
            {
                controller.Stop();
                controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
            }
        }
    }

    public static void InstallService()
    {
        // установить
        using (ProjectInstaller pi = new ProjectInstaller())
        {
            IDictionary savedState = new Hashtable();
            try
            {
                pi.Context = new InstallContext();
                pi.Context.Parameters.Add("assemblypath", Process.GetCurrentProcess().MainModule.FileName);
                foreach (Installer i in pi.Installers)
                    i.Context = pi.Context;
                pi.Install(savedState);
                pi.Commit(savedState);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                pi.Rollback(savedState);
            }
        }
    }

    public static void RemoveService()
    {
        using (ProjectInstaller pi = new ProjectInstaller())
        {
            try
            {
                pi.Context = new InstallContext();
                pi.Context.Parameters.Add("assemblypath", Process.GetCurrentProcess().MainModule.FileName);
                foreach (Installer i in pi.Installers)
                    i.Context = pi.Context;
                pi.Uninstall(null);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
    }

    public static bool IsUserAdministrator()
    {
        var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }
}
[RunInstaller(true)]
public class ProjectInstaller : Installer
{
    public ProjectInstaller()
    {
        var processInstaller = new ServiceProcessInstaller
        {
            Account = ServiceAccount.LocalSystem,
        };

        var serviceInstaller = new ServiceInstaller
        {
            ServiceName = "TorHost",
            DisplayName = "Tor Host",
            Description = "Tor Host for services",
            StartType = ServiceStartMode.Automatic,
            //DelayedAutoStart = true,
        };
        Installers.AddRange(
        [
            processInstaller,
            serviceInstaller
        ]);
    }
}
