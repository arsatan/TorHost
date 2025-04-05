rem connect to SSH Server:

rem ssh -v -i ./OpenSSH/ssh/authorized_keys/id_rsa -o "ProxyCommand=./Ncat/ncat.exe --proxy 127.0.0.1:9050 --proxy-type socks5 %h %p" ssh@y6px4gw3zhmmfwklkg6pgrpb4rs3cdmftlkeitw4peexhdkbaptnn6yd.onion
ssh -v -i %USERPROFILE%/id_rsa -o "ProxyCommand=./Ncat/ncat.exe --proxy 127.0.0.1:9050 --proxy-type socks5 %h %p" username@y6px4gw3zhmmfwklkg6pgrpb4rs3cdmftlkeitw4peexhdkbaptnn6yd.onion



