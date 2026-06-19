Get-AppxPackage *Asteriq* | Remove-AppxPackage
Get-ChildItem Cert:\LocalMachine\TrustedPeople | ? { $_.Subject -eq "CN=F6DD9498-A079-4880-979F-302321C6D1DC" } | Remove-Item
Get-ChildItem Cert:\CurrentUser\My | ? { $_.Subject -eq "CN=F6DD9498-A079-4880-979F-302321C6D1DC" } | Remove-Item