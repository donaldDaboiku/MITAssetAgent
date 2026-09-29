#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Silent install / upgrade of MIT Asset Agent Windows Service.

.EXAMPLE
  .\Install-MITAssetAgent.ps1 -PublishDir "." -ConfigFile ".\install-config.json"

.EXAMPLE
  .\Install-MITAssetAgent.ps1 -PublishDir "C:\Deploy\MITAssetAgent" -SupabaseUrl "https://xxx.supabase.co" -EnrollmentKey "..." -WorkspaceId "main"
#>
param(
  [Parameter(Mandatory = $true)][string]$PublishDir,
  [string]$ConfigFile = "",
  [string]$SupabaseUrl = "",
  [string]$EnrollmentKey = "",
  [string]$WorkspaceId = "main",
  [int]$HeartbeatIntervalMinutes = 5,
  [string]$InstallRoot = "C:\Program Files\MITAssetAgent",
  [string]$DataDir = "C:\ProgramData\MITAssetAgent"
)

$ErrorActionPreference = "Stop"
$ServiceName = "MITAssetAgent"
$DisplayName = "MIT Asset Agent"

# Load install-config.json when present (Install.cmd path). CLI args override file values.
if ($ConfigFile -and (Test-Path -LiteralPath $ConfigFile)) {
  $cfg = Get-Content -LiteralPath $ConfigFile -Raw -Encoding UTF8 | ConvertFrom-Json
  if (-not $SupabaseUrl -and $cfg.SupabaseUrl) { $SupabaseUrl = [string]$cfg.SupabaseUrl }
  if (-not $EnrollmentKey -and $cfg.EnrollmentKey) { $EnrollmentKey = [string]$cfg.EnrollmentKey }
  if ($cfg.WorkspaceId) { $WorkspaceId = [string]$cfg.WorkspaceId }
  if ($cfg.HeartbeatIntervalMinutes) { $HeartbeatIntervalMinutes = [int]$cfg.HeartbeatIntervalMinutes }
}

$SupabaseUrl = $SupabaseUrl.Trim()
$EnrollmentKey = $EnrollmentKey.Trim()
$WorkspaceId = if ($WorkspaceId.Trim()) { $WorkspaceId.Trim() } else { "main" }

if (-not $SupabaseUrl -or $SupabaseUrl -match 'YOUR_PROJECT') {
  throw "SupabaseUrl is required. Set it in install-config.json or pass -SupabaseUrl."
}
if (-not $EnrollmentKey -or $EnrollmentKey -match 'YOUR_AGENT_ENROLLMENT') {
  throw "EnrollmentKey is required. Set it in install-config.json or pass -EnrollmentKey."
}

Write-Host "Installing $DisplayName..."

$logsDir = Join-Path $DataDir "logs"
$updatesDir = Join-Path $DataDir "updates"
New-Item -ItemType Directory -Force -Path $InstallRoot | Out-Null
New-Item -ItemType Directory -Force -Path $logsDir | Out-Null
New-Item -ItemType Directory -Force -Path $updatesDir | Out-Null

# Harden data dir: SYSTEM + Administrators only.
try {
  $acl = New-Object System.Security.AccessControl.DirectorySecurity
  $acl.SetAccessRuleProtection($true, $false)
  foreach ($id in @("NT AUTHORITY\SYSTEM", "BUILTIN\Administrators")) {
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
      $id, "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow")
    $acl.AddAccessRule($rule)
  }
  Set-Acl -Path $DataDir -AclObject $acl
} catch {
  Write-Warning ("Could not tighten ACL on {0}: {1}" -f $DataDir, $_.Exception.Message)
}

# Stop existing service/process BEFORE copy so Program Files DLLs are not locked.
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
  Write-Host "Stopping existing $ServiceName service..."
  Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
  sc.exe delete $ServiceName | Out-Null
}
Get-Process -Name "MITAssetAgent" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
# Wait for file handles (clrjit.dll etc.) to release after stop/delete.
$unlockDeadline = (Get-Date).AddSeconds(20)
$lockProbe = Join-Path $InstallRoot "clrjit.dll"
while ((Get-Date) -lt $unlockDeadline) {
  if (-not (Test-Path -LiteralPath $lockProbe)) { break }
  try {
    $fs = [IO.File]::Open($lockProbe, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    $fs.Close()
    break
  } catch {
    Start-Sleep -Milliseconds 500
  }
}

Copy-Item -Path (Join-Path $PublishDir "*") -Destination $InstallRoot -Recurse -Force

# Never leave install secrets under Program Files.
foreach ($name in @(
  "install-config.json",
  "install-config.example.json",
  "install-log.txt",
  "Install.cmd",
  "Install-MITAssetAgent.ps1"
)) {
  Remove-Item -LiteralPath (Join-Path $InstallRoot $name) -Force -ErrorAction SilentlyContinue
}
# Keep Uninstall-MITAssetAgent.ps1 under Program Files for later removal.

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
if (-not (Test-Path -LiteralPath $exe)) {
  throw "MITAssetAgent.exe not found in $InstallRoot. Publish the Service project first."
}

# Register Event Log source while we have admin.
try {
  if (-not [System.Diagnostics.EventLog]::SourceExists($DisplayName)) {
    [System.Diagnostics.EventLog]::CreateEventSource($DisplayName, "Application")
    Write-Host "Registered Event Log source: $DisplayName"
  }
} catch {
  Write-Warning ("Could not register Event Log source (file logging will still work): {0}" -f $_.Exception.Message)
}

New-Service -Name $ServiceName -BinaryPathName ('"{0}"' -f $exe) -DisplayName $DisplayName -StartupType Automatic -Description "Reports device presence to MIT Asset"
sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null
try {
  Start-Service -Name $ServiceName
} catch {
  $tail = ""
  $latest = Get-ChildItem -Path $logsDir -Filter "*.log" -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
  if ($latest) { $tail = (Get-Content -LiteralPath $latest.FullName -Tail 20) -join [Environment]::NewLine }
  throw ("Service installed but failed to start. {0}{1}Log tail:{1}{2}" -f $_.Exception.Message, [Environment]::NewLine, $tail)
}

# Wait for first registration (token.dpapi), then scrub enrollment key from disk.
$tokenPath = Join-Path $DataDir "token.dpapi"
$appsettingsPath = Join-Path $InstallRoot "appsettings.json"
$deadline = (Get-Date).AddSeconds(90)
Write-Host "Waiting for device registration (up to 90s)..."
while ((Get-Date) -lt $deadline) {
  if (Test-Path -LiteralPath $tokenPath) { break }
  Start-Sleep -Seconds 3
}

function Clear-EnrollmentKeyInAppsettings([string]$path) {
  if (-not (Test-Path -LiteralPath $path)) { return }
  $json = Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
  if ($json.Agent) {
    $json.Agent.EnrollmentKey = ""
    ($json | ConvertTo-Json -Depth 6) | Set-Content -LiteralPath $path -Encoding UTF8
  }
}

if (Test-Path -LiteralPath $tokenPath) {
  Clear-EnrollmentKeyInAppsettings $appsettingsPath
  Write-Host "Registered OK. Enrollment key cleared from appsettings.json."
} else {
  Write-Warning ("No token yet after 90s - check {0}. Enrollment key left for retry; agent will clear it after successful register." -f $logsDir)
}

Write-Host "Installed and started $DisplayName."
Write-Host ("Data: {0}  Logs: {1}" -f $DataDir, $logsDir)
