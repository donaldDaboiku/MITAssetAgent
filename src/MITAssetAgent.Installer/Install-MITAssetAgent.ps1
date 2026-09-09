#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Silent install / upgrade of MIT Asset Agent Windows Service.

.EXAMPLE
  .\Install-MITAssetAgent.ps1 -PublishDir "C:\Deploy\MITAssetAgent" -SupabaseUrl "https://xxx.supabase.co" -EnrollmentKey "..." -WorkspaceId "main"
#>
param(
  [Parameter(Mandatory = $true)][string]$PublishDir,
  [Parameter(Mandatory = $true)][string]$SupabaseUrl,
  [Parameter(Mandatory = $true)][string]$EnrollmentKey,
  [string]$WorkspaceId = "main",
  [int]$HeartbeatIntervalMinutes = 5,
  [string]$InstallRoot = "C:\Program Files\MITAssetAgent",
  [string]$DataDir = "C:\ProgramData\MITAssetAgent"
)

$ErrorActionPreference = "Stop"
$ServiceName = "MITAssetAgent"
$DisplayName = "MIT Asset Agent"

Write-Host "Installing $DisplayName..."

New-Item -ItemType Directory -Force -Path $InstallRoot | Out-Null
New-Item -ItemType Directory -Force -Path "$DataDir\logs" | Out-Null
New-Item -ItemType Directory -Force -Path "$DataDir\updates" | Out-Null

Copy-Item -Path (Join-Path $PublishDir "*") -Destination $InstallRoot -Recurse -Force

$appsettings = @{
  Agent = @{
    SupabaseUrl = $SupabaseUrl.TrimEnd("/")
    WorkspaceId = $WorkspaceId
    EnrollmentKey = $EnrollmentKey
    HeartbeatIntervalMinutes = $HeartbeatIntervalMinutes
    DataDirectory = $DataDir
    AgentVersion = "1.0.0"
  }
  Logging = @{
    LogLevel = @{
      Default = "Information"
      "Microsoft.Hosting.Lifetime" = "Information"
    }
  }
} | ConvertTo-Json -Depth 6

Set-Content -Path (Join-Path $InstallRoot "appsettings.json") -Value $appsettings -Encoding UTF8

$exe = Join-Path $InstallRoot "MITAssetAgent.exe"
if (-not (Test-Path $exe)) {
  throw "MITAssetAgent.exe not found in $InstallRoot. Publish the Service project first."
}

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
  Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
  sc.exe delete $ServiceName | Out-Null
  Start-Sleep -Seconds 2
}

New-Service -Name $ServiceName -BinaryPathName "`"$exe`"" -DisplayName $DisplayName -StartupType Automatic -Description "Reports device presence to MIT Asset"
sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null
Start-Service -Name $ServiceName

Write-Host "Installed and started $DisplayName."
Write-Host "Data: $DataDir  Logs: $DataDir\logs"
