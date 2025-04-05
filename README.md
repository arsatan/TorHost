# TorHost
**TorHost** – A Windows service for accessing devices running Windows OS (Windows 7+) via Tor.

**Launching**:  
TorHost.exe – Launch in console mode.  
TorHost.exe -i – Install the TorHost service and start it.  
TorHost.exe -r – Remove the TorHost service.

**After the first launch of TorHost**:

A hostname file will be generated in the *Tor\hidden_service* subdirectory. This file contains the onion address of your Tor hidden service. After a few minutes of waiting, the following services will become accessible:

 - **WebAPI**: <your_tor_service_onion_address>:80

 	Availability check: <your_tor_service_onion_address>:80/api/tor/status

 - **WebServer**: <your_tor_service_onion_address>:81
 - **SshServer**: <your_tor_service_onion_address>:22

SSH keys (id_rsa and id_rsa.pub) will be generated in the user’s Windows directory.

**For SSH access**, the SSH client must be configured to use the Tor network. TorHost can also act as a preconfigured SSH client:  
After launching TorHost on the client device, run the following command from the TorHost directory:

*OpenSSH\ssh -v -i <path_to_ssh_private_key>/id_rsa -o "ProxyCommand=./Ncat/ncat.exe --proxy 127.0.0.1:9050 --proxy-type socks5 %h %p" <ssh_username>@<remote_tor_service_onion_address>*

**Note**: The connection may not succeed on the first attempt.

**Port remapping** is configured in the *appsettings.json* file. Port mappings are defined in the *Tor\torrc.template* file.

The *appsettings.json* file can include the *CommandServerUrl* parameter: the onion address and port of a remote Tor service where authorization data will be sent upon launching TorHost.

Example:
*"CommandServerUrl": "http://y8sv4gw4zhmmfwklkg6pgrpb4rs3cdmftlkeitw4peexhdkbaptnn6yd.onion:80"*

Data is sent to the specified address and WebAPI-port, where another TorHost service must be running. The received data is stored in the *Data\\<onion_address_of_the_source_tor_service>* subdirectory.
