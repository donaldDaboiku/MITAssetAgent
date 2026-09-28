# Deployment guides

## 0. Prebuilt package (recommended)

`dist/MITAssetAgent-1.0.0-win-x64.zip` is self-contained (no .NET needed on target PCs).

### Easy install (double‑click)

1. Extract the zip anywhere (Desktop / USB — **not** into Program Files yourself).
2. Copy `install-config.example.json` → `install-config.json` (first run of `Install.cmd` can create this and open Notepad).
3. Edit `install-config.json`: set `SupabaseUrl` and `EnrollmentKey`.
4. Double‑click **`Install.cmd`** and accept the UAC / Administrator prompt.
5. Wait until it says registered / done. Check **Services → MIT Asset Agent** and MIT Asset → **Agents**.

Do **not** double‑click `MITAssetAgent.exe`.

The installer copies files to `C:\Program Files\MITAssetAgent`, starts the service, waits for registration, then **clears the enrollment key** from `appsettings.json`. `install-config.json` is not left under Program Files.

## 1. Manual (PowerShell)

1. Install [.NET 8 SDK](https://dotnet.microsoft.com/download) on a build PC.
2. From `MITAssetAgent/src`:

```powershell
dotnet publish MITAssetAgent.Service\MITAssetAgent.Service.csproj -c Release -r win-x64 --self-contained true -o .\publish
```

3. On the target PC (admin), either use `Install.cmd` / `install-config.json`, or:

```powershell
.\Install-MITAssetAgent.ps1 `
  -PublishDir "." `
  -SupabaseUrl "https://YOUR_REF.supabase.co" `
  -EnrollmentKey "YOUR_ENROLLMENT_KEY" `
  -WorkspaceId "main"
```

## 2. Silent / scripted

`Install-MITAssetAgent.ps1` is silent (no UI). Exit code 0 = success. Prefer `-ConfigFile` so secrets are not on the command line.

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
