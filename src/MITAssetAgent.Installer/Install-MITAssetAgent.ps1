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
  $cfg = Get-Content -LiteralPath $ConfigFile -Raw | ConvertFrom-Json
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

New-Item -ItemType Directory -Force -Path $InstallRoot | Out-Null
New-Item -ItemType Directory -Force -Path "$DataDir\logs" | Out-Null
New-Item -ItemType Directory -Force -Path "$DataDir\updates" | Out-Null

# Harden data dir: SYSTEM + Administrators only (enrollment leftovers / token / queue).
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
  Write-Warning "Could not tighten ACL on $DataDir : $($_.Exception.Message)"
}

Copy-Item -Path (Join-Path $PublishDir "*") -Destination $InstallRoot -Recurse -Force

# Never leave install secrets under Program Files.
Remove-Item -LiteralPath (Join-Path $InstallRoot "install-config.json") -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path $InstallRoot "install-config.example.json") -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path $InstallRoot "install-log.txt") -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path $InstallRoot "Install.cmd") -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path $InstallRoot "Install-MITAssetAgent.ps1") -Force -ErrorAction SilentlyContinue
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
if (-not (Test-Path $exe)) {
  throw "MITAssetAgent.exe not found in $InstallRoot. Publish the Service project first."
}

# Register Event Log source while we have admin (agent itself must not create it at runtime).
try {
  if (-not [System.Diagnostics.EventLog]::SourceExists($DisplayName)) {
    [System.Diagnostics.EventLog]::CreateEventSource($DisplayName, "Application")
    Write-Host "Registered Event Log source: $DisplayName"
  }
} catch {
  Write-Warning "Could not register Event Log source (file logging will still work): $($_.Exception.Message)"
}

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
  Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
  sc.exe delete $ServiceName | Out-Null
  Start-Sleep -Seconds 2
}

New-Service -Name $ServiceName -BinaryPathName "`"$exe`"" -DisplayName $DisplayName -StartupType Automatic -Description "Reports device presence to MIT Asset"
sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null
try {
  Start-Service -Name $ServiceName
} catch {
  $tail = ""
  $latest = Get-ChildItem -Path (Join-Path $DataDir "logs") -Filter "*.log" -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
  if ($latest) { $tail = (Get-Content $latest.FullName -Tail 20) -join "`n" }
  throw "Service installed but failed to start. $($_.Exception.Message)`nLog tail:`n$tail"
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
  $json = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
  if ($json.Agent) {
    $json.Agent.EnrollmentKey = ""
    ($json | ConvertTo-Json -Depth 6) | Set-Content -LiteralPath $path -Encoding UTF8
  }
}

if (Test-Path -LiteralPath $tokenPath) {
  Clear-EnrollmentKeyInAppsettings $appsettingsPath
  Write-Host "Registered OK. Enrollment key cleared from appsettings.json."
} else {
  Write-Warning "No token yet after 90s — check $DataDir\logs. Enrollment key left for retry; agent will clear it after successful register."
}

Write-Host "Installed and started $DisplayName."
Write-Host "Data: $DataDir  Logs: $DataDir\logs"
