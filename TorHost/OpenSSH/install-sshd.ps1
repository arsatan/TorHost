﻿# @manojampalam - authored initial script
# @friism - Fixed issue with invalid SDDL on Set-Acl
# @manojampalam - removed ntrights.exe dependency
# @bingbing8 - removed secedit.exe dependency
# @tessgauthier - added permissions check for %programData%/ssh
# @tessgauthier - added update to system path for scp/sftp discoverability

[CmdletBinding(SupportsShouldProcess=$true, ConfirmImpact="High")]
param ()
Set-StrictMode -Version 2.0

$ErrorActionPreference = 'Stop'

if (!([bool]([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")))
{
    throw "You must be running as an administrator, please restart as administrator"
}

$scriptpath = $MyInvocation.MyCommand.Path
$scriptdir = Split-Path $scriptpath

$sshdpath = Join-Path $scriptdir "sshd.exe"
$sshagentpath = Join-Path $scriptdir "ssh-agent.exe"
$etwman = Join-Path $scriptdir "openssh-events.man"

if (-not (Test-Path $sshdpath)) {
    throw "sshd.exe is not present in script path"
}

if (Get-Service sshd -ErrorAction SilentlyContinue) 
{
   Stop-Service sshd
   sc.exe delete sshd 1>$null
}

if (Get-Service ssh-agent -ErrorAction SilentlyContinue) 
{
   Stop-Service ssh-agent
   sc.exe delete ssh-agent 1>$null
}

# Unregister etw provider
# PowerShell 7.3+ has new/different native command argument parsing
if ($PSVersiontable.PSVersion -le '7.2.9') {
    wevtutil um `"$etwman`"
}
else {
    wevtutil um "$etwman"
}

# adjust provider resource path in instrumentation manifest
[XML]$xml = Get-Content $etwman
$xml.instrumentationManifest.instrumentation.events.provider.resourceFileName = "$sshagentpath"
$xml.instrumentationManifest.instrumentation.events.provider.messageFileName = "$sshagentpath"

$streamWriter = $null
$xmlWriter = $null
try {
    $streamWriter = new-object System.IO.StreamWriter($etwman)
    $xmlWriter = [System.Xml.XmlWriter]::Create($streamWriter)    
    $xml.Save($xmlWriter)
}
finally {
    if($streamWriter) {
        $streamWriter.Close()
    }
}

# Fix the registry permissions
If ($PSVersiontable.PSVersion.Major -le 2) {$PSScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path}
Import-Module $PSScriptRoot\OpenSSHUtils -Force
Enable-Privilege SeRestorePrivilege | out-null

$sshRootRegPath="HKLM:SOFTWARE/Openssh"
if (Test-Path $sshRootRegPath)
{
    $sshRootAcl=Get-Acl $sshRootRegPath
    # SDDL - FullAcess to System and Builtin/Admins and read only access to Authenticated users
    $sshRootAcl.SetSecurityDescriptorSddlForm("O:BAG:SYD:P(A;OICI;KR;;;AU)(A;OICI;KA;;;SY)(A;OICI;KA;;;BA)")
    Set-Acl $sshRootRegPath $sshRootAcl
}

$sshAgentRegPath="HKLM:SOFTWARE/Openssh/agent"
if (Test-Path $sshAgentRegPath)
{
    $sshAgentAcl=Get-Acl $sshAgentRegPath
    # SDDL - FullAcess to System and Builtin/Admins.
    $sshAgentAcl.SetSecurityDescriptorSddlForm("O:BAG:SYD:P(A;OICI;KA;;;SY)(A;OICI;KA;;;BA)")
    Set-Acl $sshAgentRegPath  $sshAgentAcl
}

#Fix permissions for moduli file
$moduliPath = Join-Path $PSScriptRoot "moduli"
if (Test-Path $moduliPath -PathType Leaf)
{
    # if user calls .\install-sshd.ps1 with -confirm, use that
    # otherwise, need to preserve legacy behavior
    if (-not $PSBoundParameters.ContainsKey('confirm'))
    {
        $PSBoundParameters.add('confirm', $false)
    }
    Repair-ModuliFilePermission -FilePath $moduliPath @psBoundParameters
}

# If %programData%/ssh folder already exists, verify and, if necessary and approved by user, fix permissions 
$sshProgDataPath = Join-Path $env:ProgramData "ssh"
if (Test-Path $sshProgDataPath)
{
    # SSH Folder - owner: System or Admins; full access: System, Admins; read or readandexecute/synchronize permissible: Authenticated Users
    Repair-SSHFolderPermission -FilePath $sshProgDataPath @psBoundParameters
    # Files in SSH Folder (excluding private key files) 
    # owner: System or Admins; full access: System, Admins; read/readandexecute/synchronize permissable: Authenticated Users
    $privateKeyFiles = @("ssh_host_dsa_key", "ssh_host_ecdsa_key", "ssh_host_ed25519_key", "ssh_host_rsa_key")
    Get-ChildItem -Path (Join-Path $sshProgDataPath '*') -Recurse -Exclude ($privateKeyFiles) -Force | ForEach-Object {
        Repair-SSHFolderFilePermission -FilePath $_.FullName @psBoundParameters
    }
    # Private key files - owner: System or Admins; full access: System, Admins
    Get-ChildItem -Path (Join-Path $sshProgDataPath '*') -Recurse -Include $privateKeyFiles -Force | ForEach-Object {
        Repair-SSHFolderPrivateKeyPermission -FilePath $_.FullName @psBoundParameters
    }
}

# Register etw provider
# PowerShell 7.3+ has new/different native command argument parsing
if ($PSVersiontable.PSVersion -le '7.2.9') {
    wevtutil im `"$etwman`"
} else {
    wevtutil im "$etwman"
}

$agentDesc = "Agent to hold private keys used for public key authentication."
New-Service -Name ssh-agent -DisplayName "OpenSSH Authentication Agent" -BinaryPathName "`"$sshagentpath`"" -Description $agentDesc -StartupType Manual | Out-Null
sc.exe sdset ssh-agent "D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)(A;;RP;;;AU)"
sc.exe privs ssh-agent SeAssignPrimaryTokenPrivilege/SeTcbPrivilege/SeBackupPrivilege/SeRestorePrivilege/SeImpersonatePrivilege

$sshdDesc = "SSH protocol based service to provide secure encrypted communications between two untrusted hosts over an insecure network."
New-Service -Name sshd -DisplayName "OpenSSH SSH Server" -BinaryPathName "`"$sshdpath`"" -Description $sshdDesc -StartupType Manual | Out-Null
sc.exe privs sshd SeAssignPrimaryTokenPrivilege/SeTcbPrivilege/SeBackupPrivilege/SeRestorePrivilege/SeImpersonatePrivilege

Write-Host -ForegroundColor Green "sshd and ssh-agent services successfully installed"

# add folder to system PATH
Add-MachinePath -FilePath $scriptdir @psBoundParameters

# SIG # Begin signature block
# MIIoRgYJKoZIhvcNAQcCoIIoNzCCKDMCAQExDzANBglghkgBZQMEAgEFADB5Bgor
# BgEEAYI3AgEEoGswaTA0BgorBgEEAYI3AgEeMCYCAwEAAAQQH8w7YFlLCE63JNLG
# KX7zUQIBAAIBAAIBAAIBAAIBADAxMA0GCWCGSAFlAwQCAQUABCDqyAA6rBgCkiW1
# wpwHssx6oT8xD5y7NUpCc0q+1qfw6KCCDXYwggX0MIID3KADAgECAhMzAAADrzBA
# DkyjTQVBAAAAAAOvMA0GCSqGSIb3DQEBCwUAMH4xCzAJBgNVBAYTAlVTMRMwEQYD
# VQQIEwpXYXNoaW5ndG9uMRAwDgYDVQQHEwdSZWRtb25kMR4wHAYDVQQKExVNaWNy
# b3NvZnQgQ29ycG9yYXRpb24xKDAmBgNVBAMTH01pY3Jvc29mdCBDb2RlIFNpZ25p
# bmcgUENBIDIwMTEwHhcNMjMxMTE2MTkwOTAwWhcNMjQxMTE0MTkwOTAwWjB0MQsw
# CQYDVQQGEwJVUzETMBEGA1UECBMKV2FzaGluZ3RvbjEQMA4GA1UEBxMHUmVkbW9u
# ZDEeMBwGA1UEChMVTWljcm9zb2Z0IENvcnBvcmF0aW9uMR4wHAYDVQQDExVNaWNy
# b3NvZnQgQ29ycG9yYXRpb24wggEiMA0GCSqGSIb3DQEBAQUAA4IBDwAwggEKAoIB
# AQDOS8s1ra6f0YGtg0OhEaQa/t3Q+q1MEHhWJhqQVuO5amYXQpy8MDPNoJYk+FWA
# hePP5LxwcSge5aen+f5Q6WNPd6EDxGzotvVpNi5ve0H97S3F7C/axDfKxyNh21MG
# 0W8Sb0vxi/vorcLHOL9i+t2D6yvvDzLlEefUCbQV/zGCBjXGlYJcUj6RAzXyeNAN
# xSpKXAGd7Fh+ocGHPPphcD9LQTOJgG7Y7aYztHqBLJiQQ4eAgZNU4ac6+8LnEGAL
# go1ydC5BJEuJQjYKbNTy959HrKSu7LO3Ws0w8jw6pYdC1IMpdTkk2puTgY2PDNzB
# tLM4evG7FYer3WX+8t1UMYNTAgMBAAGjggFzMIIBbzAfBgNVHSUEGDAWBgorBgEE
# AYI3TAgBBggrBgEFBQcDAzAdBgNVHQ4EFgQURxxxNPIEPGSO8kqz+bgCAQWGXsEw
# RQYDVR0RBD4wPKQ6MDgxHjAcBgNVBAsTFU1pY3Jvc29mdCBDb3Jwb3JhdGlvbjEW
# MBQGA1UEBRMNMjMwMDEyKzUwMTgyNjAfBgNVHSMEGDAWgBRIbmTlUAXTgqoXNzci
# tW2oynUClTBUBgNVHR8ETTBLMEmgR6BFhkNodHRwOi8vd3d3Lm1pY3Jvc29mdC5j
# b20vcGtpb3BzL2NybC9NaWNDb2RTaWdQQ0EyMDExXzIwMTEtMDctMDguY3JsMGEG
# CCsGAQUFBwEBBFUwUzBRBggrBgEFBQcwAoZFaHR0cDovL3d3dy5taWNyb3NvZnQu
# Y29tL3BraW9wcy9jZXJ0cy9NaWNDb2RTaWdQQ0EyMDExXzIwMTEtMDctMDguY3J0
# MAwGA1UdEwEB/wQCMAAwDQYJKoZIhvcNAQELBQADggIBAISxFt/zR2frTFPB45Yd
# mhZpB2nNJoOoi+qlgcTlnO4QwlYN1w/vYwbDy/oFJolD5r6FMJd0RGcgEM8q9TgQ
# 2OC7gQEmhweVJ7yuKJlQBH7P7Pg5RiqgV3cSonJ+OM4kFHbP3gPLiyzssSQdRuPY
# 1mIWoGg9i7Y4ZC8ST7WhpSyc0pns2XsUe1XsIjaUcGu7zd7gg97eCUiLRdVklPmp
# XobH9CEAWakRUGNICYN2AgjhRTC4j3KJfqMkU04R6Toyh4/Toswm1uoDcGr5laYn
# TfcX3u5WnJqJLhuPe8Uj9kGAOcyo0O1mNwDa+LhFEzB6CB32+wfJMumfr6degvLT
# e8x55urQLeTjimBQgS49BSUkhFN7ois3cZyNpnrMca5AZaC7pLI72vuqSsSlLalG
# OcZmPHZGYJqZ0BacN274OZ80Q8B11iNokns9Od348bMb5Z4fihxaBWebl8kWEi2O
# PvQImOAeq3nt7UWJBzJYLAGEpfasaA3ZQgIcEXdD+uwo6ymMzDY6UamFOfYqYWXk
# ntxDGu7ngD2ugKUuccYKJJRiiz+LAUcj90BVcSHRLQop9N8zoALr/1sJuwPrVAtx
# HNEgSW+AKBqIxYWM4Ev32l6agSUAezLMbq5f3d8x9qzT031jMDT+sUAoCw0M5wVt
# CUQcqINPuYjbS1WgJyZIiEkBMIIHejCCBWKgAwIBAgIKYQ6Q0gAAAAAAAzANBgkq
# hkiG9w0BAQsFADCBiDELMAkGA1UEBhMCVVMxEzARBgNVBAgTCldhc2hpbmd0b24x
# EDAOBgNVBAcTB1JlZG1vbmQxHjAcBgNVBAoTFU1pY3Jvc29mdCBDb3Jwb3JhdGlv
# bjEyMDAGA1UEAxMpTWljcm9zb2Z0IFJvb3QgQ2VydGlmaWNhdGUgQXV0aG9yaXR5
# IDIwMTEwHhcNMTEwNzA4MjA1OTA5WhcNMjYwNzA4MjEwOTA5WjB+MQswCQYDVQQG
# EwJVUzETMBEGA1UECBMKV2FzaGluZ3RvbjEQMA4GA1UEBxMHUmVkbW9uZDEeMBwG
# A1UEChMVTWljcm9zb2Z0IENvcnBvcmF0aW9uMSgwJgYDVQQDEx9NaWNyb3NvZnQg
# Q29kZSBTaWduaW5nIFBDQSAyMDExMIICIjANBgkqhkiG9w0BAQEFAAOCAg8AMIIC
# CgKCAgEAq/D6chAcLq3YbqqCEE00uvK2WCGfQhsqa+laUKq4BjgaBEm6f8MMHt03
# a8YS2AvwOMKZBrDIOdUBFDFC04kNeWSHfpRgJGyvnkmc6Whe0t+bU7IKLMOv2akr
# rnoJr9eWWcpgGgXpZnboMlImEi/nqwhQz7NEt13YxC4Ddato88tt8zpcoRb0Rrrg
# OGSsbmQ1eKagYw8t00CT+OPeBw3VXHmlSSnnDb6gE3e+lD3v++MrWhAfTVYoonpy
# 4BI6t0le2O3tQ5GD2Xuye4Yb2T6xjF3oiU+EGvKhL1nkkDstrjNYxbc+/jLTswM9
# sbKvkjh+0p2ALPVOVpEhNSXDOW5kf1O6nA+tGSOEy/S6A4aN91/w0FK/jJSHvMAh
# dCVfGCi2zCcoOCWYOUo2z3yxkq4cI6epZuxhH2rhKEmdX4jiJV3TIUs+UsS1Vz8k
# A/DRelsv1SPjcF0PUUZ3s/gA4bysAoJf28AVs70b1FVL5zmhD+kjSbwYuER8ReTB
# w3J64HLnJN+/RpnF78IcV9uDjexNSTCnq47f7Fufr/zdsGbiwZeBe+3W7UvnSSmn
# Eyimp31ngOaKYnhfsi+E11ecXL93KCjx7W3DKI8sj0A3T8HhhUSJxAlMxdSlQy90
# lfdu+HggWCwTXWCVmj5PM4TasIgX3p5O9JawvEagbJjS4NaIjAsCAwEAAaOCAe0w
# ggHpMBAGCSsGAQQBgjcVAQQDAgEAMB0GA1UdDgQWBBRIbmTlUAXTgqoXNzcitW2o
# ynUClTAZBgkrBgEEAYI3FAIEDB4KAFMAdQBiAEMAQTALBgNVHQ8EBAMCAYYwDwYD
# VR0TAQH/BAUwAwEB/zAfBgNVHSMEGDAWgBRyLToCMZBDuRQFTuHqp8cx0SOJNDBa
# BgNVHR8EUzBRME+gTaBLhklodHRwOi8vY3JsLm1pY3Jvc29mdC5jb20vcGtpL2Ny
# bC9wcm9kdWN0cy9NaWNSb29DZXJBdXQyMDExXzIwMTFfMDNfMjIuY3JsMF4GCCsG
# AQUFBwEBBFIwUDBOBggrBgEFBQcwAoZCaHR0cDovL3d3dy5taWNyb3NvZnQuY29t
# L3BraS9jZXJ0cy9NaWNSb29DZXJBdXQyMDExXzIwMTFfMDNfMjIuY3J0MIGfBgNV
# HSAEgZcwgZQwgZEGCSsGAQQBgjcuAzCBgzA/BggrBgEFBQcCARYzaHR0cDovL3d3
# dy5taWNyb3NvZnQuY29tL3BraW9wcy9kb2NzL3ByaW1hcnljcHMuaHRtMEAGCCsG
# AQUFBwICMDQeMiAdAEwAZQBnAGEAbABfAHAAbwBsAGkAYwB5AF8AcwB0AGEAdABl
# AG0AZQBuAHQALiAdMA0GCSqGSIb3DQEBCwUAA4ICAQBn8oalmOBUeRou09h0ZyKb
# C5YR4WOSmUKWfdJ5DJDBZV8uLD74w3LRbYP+vj/oCso7v0epo/Np22O/IjWll11l
# hJB9i0ZQVdgMknzSGksc8zxCi1LQsP1r4z4HLimb5j0bpdS1HXeUOeLpZMlEPXh6
# I/MTfaaQdION9MsmAkYqwooQu6SpBQyb7Wj6aC6VoCo/KmtYSWMfCWluWpiW5IP0
# wI/zRive/DvQvTXvbiWu5a8n7dDd8w6vmSiXmE0OPQvyCInWH8MyGOLwxS3OW560
# STkKxgrCxq2u5bLZ2xWIUUVYODJxJxp/sfQn+N4sOiBpmLJZiWhub6e3dMNABQam
# ASooPoI/E01mC8CzTfXhj38cbxV9Rad25UAqZaPDXVJihsMdYzaXht/a8/jyFqGa
# J+HNpZfQ7l1jQeNbB5yHPgZ3BtEGsXUfFL5hYbXw3MYbBL7fQccOKO7eZS/sl/ah
# XJbYANahRr1Z85elCUtIEJmAH9AAKcWxm6U/RXceNcbSoqKfenoi+kiVH6v7RyOA
# 9Z74v2u3S5fi63V4GuzqN5l5GEv/1rMjaHXmr/r8i+sLgOppO6/8MO0ETI7f33Vt
# Y5E90Z1WTk+/gFcioXgRMiF670EKsT/7qMykXcGhiJtXcVZOSEXAQsmbdlsKgEhr
# /Xmfwb1tbWrJUnMTDXpQzTGCGiYwghoiAgEBMIGVMH4xCzAJBgNVBAYTAlVTMRMw
# EQYDVQQIEwpXYXNoaW5ndG9uMRAwDgYDVQQHEwdSZWRtb25kMR4wHAYDVQQKExVN
# aWNyb3NvZnQgQ29ycG9yYXRpb24xKDAmBgNVBAMTH01pY3Jvc29mdCBDb2RlIFNp
# Z25pbmcgUENBIDIwMTECEzMAAAOvMEAOTKNNBUEAAAAAA68wDQYJYIZIAWUDBAIB
# BQCgga4wGQYJKoZIhvcNAQkDMQwGCisGAQQBgjcCAQQwHAYKKwYBBAGCNwIBCzEO
# MAwGCisGAQQBgjcCARUwLwYJKoZIhvcNAQkEMSIEIEoh2MzCirk6VFI80DciRzMd
# HE0LpU94iTOXlChYbGc7MEIGCisGAQQBgjcCAQwxNDAyoBSAEgBNAGkAYwByAG8A
# cwBvAGYAdKEagBhodHRwOi8vd3d3Lm1pY3Jvc29mdC5jb20wDQYJKoZIhvcNAQEB
# BQAEggEAEMx3TbHD12aPUiiXdGM3ZYmGKhZMZ50XVGDIrj3zynjQ8HIWW6ArYgTZ
# WePsocrW7AO4rwOUvP2K21drx9LHBsAtqrkhSclFWYiwk7PDMszwonoUvLaPmXPR
# +lx3xMxK1br5qTz6KOOOsNcNuQ0czPxRV6Gwu0NyM0U2y1GovF5I6+0PSWeUvGm3
# Fdo+NmznpYGPjMF6RC7fiUyKRLzjm2OHTASNE3OQXPVMpQs2cVJXDcHcIW1SbvHd
# jygVNdCjkufRMTq4KgcV/3KFA07DJhUabyaDmGn5+2jDZKXvnnbFskoCruQOmZ6Z
# WWP7tdWKnQ/jG3S2yaTLH+d/a0eCaKGCF7AwghesBgorBgEEAYI3AwMBMYIXnDCC
# F5gGCSqGSIb3DQEHAqCCF4kwgheFAgEDMQ8wDQYJYIZIAWUDBAIBBQAwggFaBgsq
# hkiG9w0BCRABBKCCAUkEggFFMIIBQQIBAQYKKwYBBAGEWQoDATAxMA0GCWCGSAFl
# AwQCAQUABCA0p5iedpdeqFIDxLcrFloTe0jN91z115dmHCO694HZyAIGZutnnG7H
# GBMyMDI0MTAxMDE0MjczOC41MDJaMASAAgH0oIHZpIHWMIHTMQswCQYDVQQGEwJV
# UzETMBEGA1UECBMKV2FzaGluZ3RvbjEQMA4GA1UEBxMHUmVkbW9uZDEeMBwGA1UE
# ChMVTWljcm9zb2Z0IENvcnBvcmF0aW9uMS0wKwYDVQQLEyRNaWNyb3NvZnQgSXJl
# bGFuZCBPcGVyYXRpb25zIExpbWl0ZWQxJzAlBgNVBAsTHm5TaGllbGQgVFNTIEVT
# Tjo1NzFBLTA1RTAtRDk0NzElMCMGA1UEAxMcTWljcm9zb2Z0IFRpbWUtU3RhbXAg
# U2VydmljZaCCEf4wggcoMIIFEKADAgECAhMzAAAB+8vLbDdn5TCVAAEAAAH7MA0G
# CSqGSIb3DQEBCwUAMHwxCzAJBgNVBAYTAlVTMRMwEQYDVQQIEwpXYXNoaW5ndG9u
# MRAwDgYDVQQHEwdSZWRtb25kMR4wHAYDVQQKExVNaWNyb3NvZnQgQ29ycG9yYXRp
# b24xJjAkBgNVBAMTHU1pY3Jvc29mdCBUaW1lLVN0YW1wIFBDQSAyMDEwMB4XDTI0
# MDcyNTE4MzExM1oXDTI1MTAyMjE4MzExM1owgdMxCzAJBgNVBAYTAlVTMRMwEQYD
# VQQIEwpXYXNoaW5ndG9uMRAwDgYDVQQHEwdSZWRtb25kMR4wHAYDVQQKExVNaWNy
# b3NvZnQgQ29ycG9yYXRpb24xLTArBgNVBAsTJE1pY3Jvc29mdCBJcmVsYW5kIE9w
# ZXJhdGlvbnMgTGltaXRlZDEnMCUGA1UECxMeblNoaWVsZCBUU1MgRVNOOjU3MUEt
# MDVFMC1EOTQ3MSUwIwYDVQQDExxNaWNyb3NvZnQgVGltZS1TdGFtcCBTZXJ2aWNl
# MIICIjANBgkqhkiG9w0BAQEFAAOCAg8AMIICCgKCAgEAqMJWQeWAq4LwvSjYsjP0
# Uvhvm0j0aAOJiMLg0sLfxKoTXAdKD6oMuq5rF5oEiOxV+9ox0H95Q8fhoZq3x9lx
# guZyTOK4l2xtcgtJCtjXRllM2bTpjOg35RUrBy0cAloBU9GJBs7LBNrcbH6rBiOv
# qDQNicPRZwq16xyjMidU1J1AJuat9yLn7taifoD58blYEcBvkj5dH1la9zU846QD
# eOoRO6NcqHLsDx8/zVKZxP30mW6Y7RMsqtB8cGCgGwVVurOnaNLXs31qTRTyVHX8
# ppOdoSihCXeqebgJCRzG8zG/e/k0oaBjFFGl+8uFELwCyh4wK9Z5+azTzfa2GD4p
# 6ihtskXs3lnW05UKfDJhAADt6viOc0Rk/c8zOiqzh0lKpf/eWUY2o/hvcDPZNgLa
# HvyfDqb8AWaKvO36iRZSXqhSw8SxJo0TCpsbCjmtx0LpHnqbb1UF7cq09kCcfWTD
# PcN12pbYLqck0bIIfPKbc7HnrkNQks/mSbVZTnDyT3O8zF9q4DCfWesSr1akycDd
# uGxCdKBvgtJh1YxDq1skTweYx5iAWXnB7KMyls3WQZbTubTCLLt8Xn8t+slcKm5D
# kvobubmHSriuTA3wTyIy4FxamTKm0VDu9mWds8MtjUSJVwNVVlBXaQ3ZMcVjijyV
# oUNVuBY9McwYcIQK62wQ20ECAwEAAaOCAUkwggFFMB0GA1UdDgQWBBRHVSGYUNQ3
# RwOl71zIAuUjIKg1KjAfBgNVHSMEGDAWgBSfpxVdAF5iXYP05dJlpxtTNRnpcjBf
# BgNVHR8EWDBWMFSgUqBQhk5odHRwOi8vd3d3Lm1pY3Jvc29mdC5jb20vcGtpb3Bz
# L2NybC9NaWNyb3NvZnQlMjBUaW1lLVN0YW1wJTIwUENBJTIwMjAxMCgxKS5jcmww
# bAYIKwYBBQUHAQEEYDBeMFwGCCsGAQUFBzAChlBodHRwOi8vd3d3Lm1pY3Jvc29m
# dC5jb20vcGtpb3BzL2NlcnRzL01pY3Jvc29mdCUyMFRpbWUtU3RhbXAlMjBQQ0El
# MjAyMDEwKDEpLmNydDAMBgNVHRMBAf8EAjAAMBYGA1UdJQEB/wQMMAoGCCsGAQUF
# BwMIMA4GA1UdDwEB/wQEAwIHgDANBgkqhkiG9w0BAQsFAAOCAgEAwzoIKOY2dnUj
# fWuMiGoz/ovoc1e86VwWaZNFdgRmOoQuRe4nLdtZONtTHNk3Sj3nkyBszzxSbZEQ
# 0DduyKHHI5P8V87jFttGnlR0wPP22FAebbvAbutkMMVQMFzhVBWiWD0VAnu9x0fj
# ifLKDAVXLwoun5rCFqwbasXFc7H/0DPiC+DBn3tUxefvcxUCys4+DC3s8CYp7WWX
# pZ8Wb/vdBhDliHmB7pWcmsB83uc4/P2GmAI3HMkOEu7fCaSYoQhouWOr07l/KM4T
# ndylIirm8f2WwXQcFEzmUvISM6ludUwGlVNfTTJUq2bTDEd3tlDKtV9AUY3rrnFw
# HTwJryLtT4IFhvgBfND3mL1eeSakKf7xTII4Jyt15SXhHd5oI/XGjSgykgJrWA57
# rGnAC7ru3/ZbFNCMK/Jj6X8X4L6mBOYa2NGKwH4A37YGDrecJ/qXXWUYvfLYqHGf
# 8ThYl12Yg1rwSKpWLolA/B1eqBw4TRcvVY0IvNNi5sm+//HJ9Aw6NJuR/uDR7X7v
# DXicpXMlRNgFMyADb8AFIvQPdHqcRpRorY+YUGlvzeJx/2gNYyezAokbrFhACsJ2
# BfyeLyCEo6AuwEHn511PKE8dK4JvlmLSoHj7VFR3NHDk3zRkx0ExkmF8aOdpvoKh
# uwBCxoZ/JhbzSzrvZ74GVjKKIyt5FA0wggdxMIIFWaADAgECAhMzAAAAFcXna54C
# m0mZAAAAAAAVMA0GCSqGSIb3DQEBCwUAMIGIMQswCQYDVQQGEwJVUzETMBEGA1UE
# CBMKV2FzaGluZ3RvbjEQMA4GA1UEBxMHUmVkbW9uZDEeMBwGA1UEChMVTWljcm9z
# b2Z0IENvcnBvcmF0aW9uMTIwMAYDVQQDEylNaWNyb3NvZnQgUm9vdCBDZXJ0aWZp
# Y2F0ZSBBdXRob3JpdHkgMjAxMDAeFw0yMTA5MzAxODIyMjVaFw0zMDA5MzAxODMy
# MjVaMHwxCzAJBgNVBAYTAlVTMRMwEQYDVQQIEwpXYXNoaW5ndG9uMRAwDgYDVQQH
# EwdSZWRtb25kMR4wHAYDVQQKExVNaWNyb3NvZnQgQ29ycG9yYXRpb24xJjAkBgNV
# BAMTHU1pY3Jvc29mdCBUaW1lLVN0YW1wIFBDQSAyMDEwMIICIjANBgkqhkiG9w0B
# AQEFAAOCAg8AMIICCgKCAgEA5OGmTOe0ciELeaLL1yR5vQ7VgtP97pwHB9KpbE51
# yMo1V/YBf2xK4OK9uT4XYDP/XE/HZveVU3Fa4n5KWv64NmeFRiMMtY0Tz3cywBAY
# 6GB9alKDRLemjkZrBxTzxXb1hlDcwUTIcVxRMTegCjhuje3XD9gmU3w5YQJ6xKr9
# cmmvHaus9ja+NSZk2pg7uhp7M62AW36MEBydUv626GIl3GoPz130/o5Tz9bshVZN
# 7928jaTjkY+yOSxRnOlwaQ3KNi1wjjHINSi947SHJMPgyY9+tVSP3PoFVZhtaDua
# Rr3tpK56KTesy+uDRedGbsoy1cCGMFxPLOJiss254o2I5JasAUq7vnGpF1tnYN74
# kpEeHT39IM9zfUGaRnXNxF803RKJ1v2lIH1+/NmeRd+2ci/bfV+AutuqfjbsNkz2
# K26oElHovwUDo9Fzpk03dJQcNIIP8BDyt0cY7afomXw/TNuvXsLz1dhzPUNOwTM5
# TI4CvEJoLhDqhFFG4tG9ahhaYQFzymeiXtcodgLiMxhy16cg8ML6EgrXY28MyTZk
# i1ugpoMhXV8wdJGUlNi5UPkLiWHzNgY1GIRH29wb0f2y1BzFa/ZcUlFdEtsluq9Q
# BXpsxREdcu+N+VLEhReTwDwV2xo3xwgVGD94q0W29R6HXtqPnhZyacaue7e3Pmri
# Lq0CAwEAAaOCAd0wggHZMBIGCSsGAQQBgjcVAQQFAgMBAAEwIwYJKwYBBAGCNxUC
# BBYEFCqnUv5kxJq+gpE8RjUpzxD/LwTuMB0GA1UdDgQWBBSfpxVdAF5iXYP05dJl
# pxtTNRnpcjBcBgNVHSAEVTBTMFEGDCsGAQQBgjdMg30BATBBMD8GCCsGAQUFBwIB
# FjNodHRwOi8vd3d3Lm1pY3Jvc29mdC5jb20vcGtpb3BzL0RvY3MvUmVwb3NpdG9y
# eS5odG0wEwYDVR0lBAwwCgYIKwYBBQUHAwgwGQYJKwYBBAGCNxQCBAweCgBTAHUA
# YgBDAEEwCwYDVR0PBAQDAgGGMA8GA1UdEwEB/wQFMAMBAf8wHwYDVR0jBBgwFoAU
# 1fZWy4/oolxiaNE9lJBb186aGMQwVgYDVR0fBE8wTTBLoEmgR4ZFaHR0cDovL2Ny
# bC5taWNyb3NvZnQuY29tL3BraS9jcmwvcHJvZHVjdHMvTWljUm9vQ2VyQXV0XzIw
# MTAtMDYtMjMuY3JsMFoGCCsGAQUFBwEBBE4wTDBKBggrBgEFBQcwAoY+aHR0cDov
# L3d3dy5taWNyb3NvZnQuY29tL3BraS9jZXJ0cy9NaWNSb29DZXJBdXRfMjAxMC0w
# Ni0yMy5jcnQwDQYJKoZIhvcNAQELBQADggIBAJ1VffwqreEsH2cBMSRb4Z5yS/yp
# b+pcFLY+TkdkeLEGk5c9MTO1OdfCcTY/2mRsfNB1OW27DzHkwo/7bNGhlBgi7ulm
# ZzpTTd2YurYeeNg2LpypglYAA7AFvonoaeC6Ce5732pvvinLbtg/SHUB2RjebYIM
# 9W0jVOR4U3UkV7ndn/OOPcbzaN9l9qRWqveVtihVJ9AkvUCgvxm2EhIRXT0n4ECW
# OKz3+SmJw7wXsFSFQrP8DJ6LGYnn8AtqgcKBGUIZUnWKNsIdw2FzLixre24/LAl4
# FOmRsqlb30mjdAy87JGA0j3mSj5mO0+7hvoyGtmW9I/2kQH2zsZ0/fZMcm8Qq3Uw
# xTSwethQ/gpY3UA8x1RtnWN0SCyxTkctwRQEcb9k+SS+c23Kjgm9swFXSVRk2XPX
# fx5bRAGOWhmRaw2fpCjcZxkoJLo4S5pu+yFUa2pFEUep8beuyOiJXk+d0tBMdrVX
# VAmxaQFEfnyhYWxz/gq77EFmPWn9y8FBSX5+k77L+DvktxW/tM4+pTFRhLy/AsGC
# onsXHRWJjXD+57XQKBqJC4822rpM+Zv/Cuk0+CQ1ZyvgDbjmjJnW4SLq8CdCPSWU
# 5nR0W2rRnj7tfqAxM328y+l7vzhwRNGQ8cirOoo6CGJ/2XBjU02N7oJtpQUQwXEG
# ahC0HVUzWLOhcGbyoYIDWTCCAkECAQEwggEBoYHZpIHWMIHTMQswCQYDVQQGEwJV
# UzETMBEGA1UECBMKV2FzaGluZ3RvbjEQMA4GA1UEBxMHUmVkbW9uZDEeMBwGA1UE
# ChMVTWljcm9zb2Z0IENvcnBvcmF0aW9uMS0wKwYDVQQLEyRNaWNyb3NvZnQgSXJl
# bGFuZCBPcGVyYXRpb25zIExpbWl0ZWQxJzAlBgNVBAsTHm5TaGllbGQgVFNTIEVT
# Tjo1NzFBLTA1RTAtRDk0NzElMCMGA1UEAxMcTWljcm9zb2Z0IFRpbWUtU3RhbXAg
# U2VydmljZaIjCgEBMAcGBSsOAwIaAxUABHHn7NCGusZz2RfVbyuwYwPykBWggYMw
# gYCkfjB8MQswCQYDVQQGEwJVUzETMBEGA1UECBMKV2FzaGluZ3RvbjEQMA4GA1UE
# BxMHUmVkbW9uZDEeMBwGA1UEChMVTWljcm9zb2Z0IENvcnBvcmF0aW9uMSYwJAYD
# VQQDEx1NaWNyb3NvZnQgVGltZS1TdGFtcCBQQ0EgMjAxMDANBgkqhkiG9w0BAQsF
# AAIFAOqyPQswIhgPMjAyNDEwMTAxMTQ2MTlaGA8yMDI0MTAxMTExNDYxOVowdzA9
# BgorBgEEAYRZCgQBMS8wLTAKAgUA6rI9CwIBADAKAgEAAgIgegIB/zAHAgEAAgIT
# /zAKAgUA6rOOiwIBADA2BgorBgEEAYRZCgQCMSgwJjAMBgorBgEEAYRZCgMCoAow
# CAIBAAIDB6EgoQowCAIBAAIDAYagMA0GCSqGSIb3DQEBCwUAA4IBAQDP60A1HV/1
# AReLYj3zYPbRzCAyPqlAjNR+jfhfn+jzHVLBXW3KsY4+njJmxXB1zfSXYErK8Cyn
# RQB60faPhRuJyZmrbc6IB55uOy6kMEVLSXK6AVztFn9VH/Pj0Aq/YNlwKwsor06l
# 7uuhJThMgsJojdqffyJzKJ0vBj8xcRippVx5VYc8QP/xxk9XSgaPxG5SuG8v5D7u
# JUh8EtuJFm01bDL7V7vM8Bc5zxzbGXHHWwagifeUZzPSv9AdFKxpU7je4GjR3K6W
# dpU2gkX0FvvzWUtNlaomG2MEvMxFTVib2ddf00fYYrqm17piB0uGb7sO77J9MNTo
# lUJq1FCyDbxNMYIEDTCCBAkCAQEwgZMwfDELMAkGA1UEBhMCVVMxEzARBgNVBAgT
# Cldhc2hpbmd0b24xEDAOBgNVBAcTB1JlZG1vbmQxHjAcBgNVBAoTFU1pY3Jvc29m
# dCBDb3Jwb3JhdGlvbjEmMCQGA1UEAxMdTWljcm9zb2Z0IFRpbWUtU3RhbXAgUENB
# IDIwMTACEzMAAAH7y8tsN2flMJUAAQAAAfswDQYJYIZIAWUDBAIBBQCgggFKMBoG
# CSqGSIb3DQEJAzENBgsqhkiG9w0BCRABBDAvBgkqhkiG9w0BCQQxIgQg/I3ciU/N
# kA2G1EYfKj/uAj0lWcdTAbQqnnf709QxmMYwgfoGCyqGSIb3DQEJEAIvMYHqMIHn
# MIHkMIG9BCA52wKr/KCFlVNYiWsCLsB4qhjEYEP3xHqYqDu1SSTlGDCBmDCBgKR+
# MHwxCzAJBgNVBAYTAlVTMRMwEQYDVQQIEwpXYXNoaW5ndG9uMRAwDgYDVQQHEwdS
# ZWRtb25kMR4wHAYDVQQKExVNaWNyb3NvZnQgQ29ycG9yYXRpb24xJjAkBgNVBAMT
# HU1pY3Jvc29mdCBUaW1lLVN0YW1wIFBDQSAyMDEwAhMzAAAB+8vLbDdn5TCVAAEA
# AAH7MCIEIDpBwjm7JT4J2AcEDlQMLqpCb3hquhMlOIh6U342e+T9MA0GCSqGSIb3
# DQEBCwUABIICADqSqUVYA4MfwvOCfEG4pAbOf9zCgSeeu35zwReGaqw1Y8twkhEH
# 8XkG2H3cYW5uqpuGsA/2JrVIc+d3dZ1crj4hbvJFJjDTwIVgjmuJKnYCCbJtkCm7
# mHzMXaNwz8kw6yCCV4QSuYMj7HrcYu7UnlAYeG5zEGX5eyBQgT4s/X1owTT6ne4Z
# QMgzKKIZ4E3BgdVVQOwMvVilJlb05UWW4CXY5/6wS6zGiAkju0V2j42C8nlWKF1F
# RY+nxIsHaNpdBXnhvp88+FoV/+0xqU4tTUAroTuDUSKaHevj+Xkz2YAq/sGhsRAP
# GWlxM7ga3eiIp6k7V4Wq9t7d/+urp+KzJ3m99nOD+s9S+Wf5pgL98EhtJ9S4GPeo
# UVpm+HczScAdhWxJByOUX04RoX7iUFNDGEqHO2YgMfNtQd/y9QjLE1gy8/C2+KXp
# o9mDYz/C/xKR1F4Bb77vvUcWn2IPYHIcibgm/v4qrTCdTNdsAABiSTgbeVuXUSYw
# J2Db/lejo/vuHQ/C8YCIXB4QdnKsIEQgsX1UfTuG1gwc+eDehLMdFvuOkFZY8sLJ
# RQ8itohdiXhiuczxbmDWxIhDQdM64sePipoLnbPnhyN43HB2LdOyErvFg5YfnZHM
# +beEeedHrWwKN7w9go+DTkyDzOOXbJPv3FNRuD9hDVzh77BzmKxU9yAt
# SIG # End signature block
