#Requires -RunAsAdministrator
param([string]$ServiceName = "MITAssetAgent")
Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
sc.exe delete $ServiceName | Out-Null
Write-Host "Service removed. Program files/data left intact — delete manually if desired."
