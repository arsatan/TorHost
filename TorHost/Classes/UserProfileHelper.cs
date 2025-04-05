//using System.Security.Principal;

//WindowsIdentity.RunImpersonated(userToken, () =>
//{
//    // Этот код выполнится в контексте пользователя
//    string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
//});

using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

public class UserProfileHelper
{
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LogonUser(
        string lpszUsername,
        string lpszDomain,
        string lpszPassword,
        int dwLogonType,
        int dwLogonProvider,
        out SafeAccessTokenHandle phToken);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hHandle);

    [DllImport("userenv.dll", SetLastError = true)]
    static extern bool GetUserProfileDirectory(SafeAccessTokenHandle hToken, System.Text.StringBuilder lpProfileDir, ref uint lpcchSize);

    public static string GetActiveUserProfilePath()
    {
        string profilePath = null;
        SafeAccessTokenHandle userToken = GetActiveUserToken();

        if (userToken != null && !userToken.IsInvalid)
        {
            uint bufferSize = 256;
            System.Text.StringBuilder pathBuilder = new System.Text.StringBuilder((int)bufferSize);
            if (GetUserProfileDirectory(userToken, pathBuilder, ref bufferSize))
            {
                profilePath = pathBuilder.ToString();
            }
            userToken.Dispose();
        }

        return profilePath;
    }
    private static SafeAccessTokenHandle GetActiveUserToken()
    {
        uint sessionId = WTSGetActiveConsoleSessionId();
        IntPtr userTokenPtr = IntPtr.Zero;
        if (WTSQueryUserToken(sessionId, out userTokenPtr))
        {
            return new SafeAccessTokenHandle(userTokenPtr);
        }
        return null;
    }

    [DllImport("wtsapi32.dll", SetLastError = true)]
    static extern bool WTSQueryUserToken(uint sessionId, out IntPtr phToken);

    [DllImport("kernel32.dll")]
    static extern uint WTSGetActiveConsoleSessionId();
}
