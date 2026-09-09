# MIT Asset Agent — API

Base URL: `https://<PROJECT_REF>.supabase.co`

All agent functions are deployed with `--no-verify-jwt` (device auth is enrollment key or Bearer token).

## Secrets

| Secret | Purpose |
|--------|---------|
| `AGENT_ENROLLMENT_KEY` | Header `x-enrollment-key` for **register-agent** only |
| `AGENT_ADMIN_SECRET` | Optional; header `x-admin-secret` for revoke/regenerate (falls back to enrollment key) |
| `SUPABASE_URL` / `SUPABASE_SERVICE_ROLE_KEY` | Auto-injected on hosted Supabase |

```bash
supabase secrets set AGENT_ENROLLMENT_KEY=your-long-random-enrollment-key
supabase secrets set AGENT_ADMIN_SECRET=your-admin-secret   # optional
supabase functions deploy register-agent --no-verify-jwt
supabase functions deploy agent-heartbeat --no-verify-jwt
supabase functions deploy revoke-agent-token --no-verify-jwt
supabase functions deploy regenerate-agent-token --no-verify-jwt
```

## POST /functions/v1/register-agent

Headers: `x-enrollment-key`, `Content-Type: application/json`

Returns `{ ok, agent, token }` — **token plaintext once**.

## POST /functions/v1/agent-heartbeat

Headers: `Authorization: Bearer <device-token>`

Updates `mit_agents.last_seen` and upserts `mit_heartbeats` (PWA compatibility).

## POST /functions/v1/revoke-agent-token

Headers: `x-admin-secret`  
Body: `{ "workspaceId":"main", "agentId":"..." }`

## POST /functions/v1/regenerate-agent-token

Headers: `x-admin-secret`  
Body: `{ "workspaceId":"main", "agentId":"..." }`  
Returns new plaintext `token` once — push to the PC securely (or reinstall/re-enroll).
