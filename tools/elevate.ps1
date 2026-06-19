$ErrorActionPreference = 'Stop'

# Steps 3-4 (trust the cert + install the package) require admin. If we are not already
# elevated, relaunch this script with a UAC prompt and exit the non-elevated instance.
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Start-Process powershell.exe "-NoExit -NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`"" -Verb RunAs
    exit
}

$msix = Join-Path $PSScriptRoot "..\dist\Asteriq.msix"
$publisher = "CN=F6DD9498-A079-4880-979F-302321C6D1DC"

if (-not (Test-Path $msix)) { throw "MSIX not found at $msix - build it first with publish.ps1" }

# 1. Self-signed cert whose subject EXACTLY matches the package publisher
$cert = New-SelfSignedCertificate -Type Custom -Subject $publisher `
  -KeyUsage DigitalSignature -FriendlyName "Asteriq Test Signing" `
  -CertStoreLocation "Cert:\CurrentUser\My" `
  -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3")

# 2. Sign the MSIX with it
$signtool = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe" |
  Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
& $signtool sign /fd SHA256 /sha1 $cert.Thumbprint $msix

# 3. Trust the cert (Local Machine -> Trusted People)
Export-Certificate -Cert $cert -FilePath "$env:TEMP\AsteriqTest.cer" | Out-Null
Import-Certificate -FilePath "$env:TEMP\AsteriqTest.cer" -CertStoreLocation "Cert:\LocalMachine\TrustedPeople" | Out-Null

# 4. Install (signed + trusted now — no -AllowUnsigned)
Add-AppxPackage -Path $msix