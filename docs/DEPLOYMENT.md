# Deployment guides

## 0. Prebuilt package

`dist/MITAssetAgent-1.0.0-win-x64.zip` is a self-contained build (installer scripts included). Target PCs do not need .NET installed. Extract it and skip to the install step — `appsettings.json` in the zip holds placeholders only; the installer writes the real values.

## 1. Manual

1. Install [.NET 8 SDK](https://dotnet.microsoft.com/download) on a build PC.
2. From `MITAssetAgent/src`:

```powershell
dotnet publish MITAssetAgent.Service\MITAssetAgent.Service.csproj -c Release -r win-x64 --self-contained true -o .\publish
```

3. On the target PC (admin):

```powershell
.\MITAssetAgent.Installer\Install-MITAssetAgent.ps1 `
  -PublishDir ".\publish" `
  -SupabaseUrl "https://YOUR_REF.supabase.co" `
  -EnrollmentKey "YOUR_ENROLLMENT_KEY" `
  -WorkspaceId "main"
```

## 2. Silent / scripted

Same PowerShell installer is silent by default (no UI). Exit code 0 = success.

## 3. Active Directory GPO

1. Copy the `publish` folder + installer script to a network share (`\\fileserver\MITAssetAgent`).
2. Computer Configuration → Policies → Windows Settings → Scripts → Startup  
   Or use Immediate Task scheduled as SYSTEM.
3. Script arguments: path to `Install-MITAssetAgent.ps1` with `-PublishDir`, `-SupabaseUrl`, `-EnrollmentKey`.
4. Target an OU of workstations. Reboot or force gpupdate.

## 4. Microsoft Intune

1. Package as Win32 app (Microsoft Win32 Content Prep Tool) containing published binaries + install script.
2. Install command:

```text
powershell.exe -ExecutionPolicy Bypass -File Install-MITAssetAgent.ps1 -PublishDir .\ -SupabaseUrl https://YOUR_REF.supabase.co -EnrollmentKey YOUR_KEY -WorkspaceId main
```

3. Uninstall command: `powershell.exe -ExecutionPolicy Bypass -File Uninstall-MITAssetAgent.ps1`
4. Detection rule: service `MITAssetAgent` exists / file `C:\Program Files\MITAssetAgent\MITAssetAgent.exe`
5. Assign to device groups.

## 5. MSI note

WiX/MSIX can wrap the same published output. Prefer Win32 + Intune for speed; add WiX when you need classic GPO Software Installation (.msi).
