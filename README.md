# MIT Asset Agent (standalone)

**This project is separate from the MIT Asset web app.**

| Project | Path |
|---------|------|
| Web app (PWA) | `../MITassest/` |
| This Windows agent | `../MITAssetAgent/` (this folder) |

They share the same **Supabase project** (tables + Edge Functions), but you can version, build, and deploy them independently.

## What this is

Production Windows Service that runs on each company PC:

- Registers with a unique device token (DPAPI-protected)
- Sends heartbeats every 5 minutes (configurable)
- Starts automatically with Windows (no user login required)
- Queues heartbeats offline (SQLite)

## Build & install

See [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md), [docs/API.md](docs/API.md), [docs/SECURITY.md](docs/SECURITY.md).

```powershell
# Requires .NET 8 SDK
cd src
dotnet publish MITAssetAgent.Service\MITAssetAgent.Service.csproj -c Release -r win-x64 --self-contained true -o .\publish

# On each PC (Admin PowerShell):
.\MITAssetAgent.Installer\Install-MITAssetAgent.ps1 `
  -PublishDir ".\publish" `
  -SupabaseUrl "https://YOUR_PROJECT.supabase.co" `
  -EnrollmentKey "YOUR_AGENT_ENROLLMENT_KEY" `
  -WorkspaceId "main"
```

## Supabase (owned by this project)

- Schema: `supabase/mit_agents.sql`
- Functions: `supabase/functions/` (`register-agent`, `agent-heartbeat`, `revoke-agent-token`, `regenerate-agent-token`)

Deploy from a machine with Supabase CLI linked to your project (or copy functions into the web app’s `supabase/functions` folder if you deploy everything from there).

## Compatibility

Still upserts `mit_heartbeats` so the existing MIT Asset PWA **Network Presence** / **Agents** pages keep working.
