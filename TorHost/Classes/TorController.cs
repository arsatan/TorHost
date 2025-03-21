using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.RegularExpressions;

[ApiController]
[Route("api/[controller]")]
public class TorController : ControllerBase
{
    private readonly TorHost _torService;
    private string _dataPath = AppDomain.CurrentDomain.BaseDirectory + @"/Data/";

    public TorController(TorHost torService)
    {
        _torService = torService;
    }

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        return Ok(new
        {
            Status = "Running",
            // OnionAddress = _torService.GetOnionAddress(),
            // Uptime = DateTime.Now - _torService.StartTime
        });
    }

    [HttpPost("restart")]
    public IActionResult RestartService()
    {
        //_torService.Restart();
        return Accepted();
    }

    [HttpPost("process")]
    //public ResponseModel ProcessJson([FromBody] PostDataModel data)
    public ResponseModel ProcessJson(PostDataModel data)
    {
        var result = new ResponseModel
        {
            Status = "Success",
            Message = "Error"
        };

        //System.IO.File.WriteAllText(_dataPath + @"/request.log", data.Sender + "\r\n" + data.Data);
        string senderPath = _dataPath + data.Sender;
        try
        {
            if (data.Data.Length > 16000 || !OnionAddressValidator.IsValidV3OnionAddress(data.Sender))
                return result;
            if (!Directory.Exists(senderPath))
            {
                Directory.CreateDirectory(senderPath);
                Directory.CreateDirectory(senderPath + @"/hidden_service");
                Directory.CreateDirectory(senderPath + @"/ssh");
            }
            ServicesDataModel servicesDataModel = JsonSerializer.Deserialize<ServicesDataModel>(Convert.FromBase64String(data.Data));
            System.IO.File.WriteAllText(senderPath + @"/username", servicesDataModel.username);
            System.IO.File.WriteAllText(senderPath + @"/hidden_service/host", servicesDataModel.host);
            System.IO.File.WriteAllBytes(senderPath + @"/ssh/id_rsa", Convert.FromBase64String(servicesDataModel.id_rsa));
            System.IO.File.WriteAllBytes(senderPath + @"/ssh/id_rsa.pub", Convert.FromBase64String(servicesDataModel.id_rsa_pub));
            System.IO.File.WriteAllBytes(senderPath + @"/hidden_service/hs_ed25519_secret_key", Convert.FromBase64String(servicesDataModel.hs_ed25519_secret_key));
            System.IO.File.WriteAllBytes(senderPath + @"/hidden_service/hs_ed25519_public_key", Convert.FromBase64String(servicesDataModel.hs_ed25519_public_key));
            System.IO.File.WriteAllBytes(senderPath + @"/ssh/hs_ed25519_secret_key", Convert.FromBase64String(servicesDataModel.hs_ed25519_secret_key));
            System.IO.File.WriteAllBytes(senderPath + @"/ssh/hs_ed25519_public_key", Convert.FromBase64String(servicesDataModel.hs_ed25519_public_key));
            System.IO.File.WriteAllBytes(senderPath + @"/ssh/ssh_host_ecdsa_key", Convert.FromBase64String(servicesDataModel.ssh_host_ecdsa_key));
            System.IO.File.WriteAllBytes(senderPath + @"/ssh/ssh_host_ecdsa_key_pub", Convert.FromBase64String(servicesDataModel.ssh_host_ecdsa_key_pub));
            System.IO.File.WriteAllBytes(senderPath + @"/ssh/ssh_host_rsa_key", Convert.FromBase64String(servicesDataModel.ssh_host_rsa_key));
            System.IO.File.WriteAllBytes(senderPath + @"/ssh/ssh_host_rsa_key_pub", Convert.FromBase64String(servicesDataModel.ssh_host_rsa_key_pub));
        }
        catch (Exception ex)
        {
            System.IO.File.WriteAllText(_dataPath + @"/error.log", ex.StackTrace + "\r\n" + data);
            result.Message = "Exception";
            return result;
        }
        result.Message = "OK";
        return result;
    }
    public static class OnionAddressValidator
    {
        // Регулярное выражение для onion v3 (56 символов)
        private static readonly Regex V3OnionRegex =
            new Regex(@"^[a-z2-7]{56}\.onion$", RegexOptions.Compiled);

        // Для устаревших v2 (16 символов)
        private static readonly Regex V2OnionRegex =
            new Regex(@"^[a-z2-7]{16}\.onion$", RegexOptions.Compiled);

        /// <summary>
        /// Проверяет валидность onion-адреса (v3 или v2)
        /// </summary>
        public static bool IsValidOnionAddress(string address)
        {
            return V3OnionRegex.IsMatch(address) || V2OnionRegex.IsMatch(address);
        }

        /// <summary>
        /// Проверяет валидность только актуальных onion v3
        /// </summary>
        public static bool IsValidV3OnionAddress(string address)
        {
            return V3OnionRegex.IsMatch(address);
        }
    }
}