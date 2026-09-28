# Security

## Threat model (summary)

| Risk | Mitigation |
|------|------------|
| Shared heartbeat secret on every PC | Replaced by **unique Bearer token per agent** |
| Token theft from disk | **DPAPI LocalMachine** (`token.dpapi`); not in `config.json` |
| Server breach exposing tokens | Server stores **SHA-256 hash only** |
| Hostname spoofing as identity | Identity = **device fingerprint** (BIOS + board + MAC) + UUID `agent_id` |
| Rogue registration | Org **enrollment key** required for register-agent |
| Compromised token | Admin **revoke** / **regenerate** / **disable** agent |
| Offline abuse | Local SQLite queue with dedupe + exponential backoff |

## Enrollment key vs device token

- Enrollment key: put in `install-config.json` for `Install.cmd` (or pass to the PowerShell installer). Used **once** at registration.
- After a successful register, the installer and/or agent **clear** `Agent:EnrollmentKey` from `appsettings.json` so it does not stay on disk.
- Device token: returned at registration; never logged; DPAPI-protected under `C:\ProgramData\MITAssetAgent` (ACL: SYSTEM + Administrators).
- Do not ship a filled `install-config.json` inside the shared zip; use the example file and fill per deploy.

## Post-rollout enrollment key rotation

After PCs are registered, rotate `AGENT_ENROLLMENT_KEY` so a key left on disk cannot enroll new agents:

1. Generate a new long random key.
2. `supabase secrets set AGENT_ENROLLMENT_KEY=<new-key> --project-ref <ref>`
3. Update MIT Asset Settings → Agent enrollment key (and/or cloud workspace settings).
4. On already-installed PCs, clear `Agent:EnrollmentKey` from `C:\Program Files\MITAssetAgent\appsettings.json` (optional hygiene). Do **not** delete `token.dpapi`.
5. Existing agents keep heartbeating with their unique device tokens. Reinstall / new PCs need the **new** enrollment key.

Keep `AGENT_ADMIN_SECRET` separate if possible; rotate it only when needed for revoke/regenerate access.

## HTTPS

Agent talks only to `https://*.supabase.co`. Cleartext HTTP is not supported by design.

## Admin endpoints

Protect `revoke-agent-token` / `regenerate-agent-token` with `AGENT_ADMIN_SECRET`. Do not put that secret on endpoints.
