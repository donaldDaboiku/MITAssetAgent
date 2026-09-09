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

- Enrollment key: deploy via Intune/GPO into `appsettings.json` (or machine env). Used **once** at registration.
- Device token: returned at registration; never logged; DPAPI-protected.

## HTTPS

Agent talks only to `https://*.supabase.co`. Cleartext HTTP is not supported by design.

## Admin endpoints

Protect `revoke-agent-token` / `regenerate-agent-token` with `AGENT_ADMIN_SECRET`. Do not put that secret on endpoints.
