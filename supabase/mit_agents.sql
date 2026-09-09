-- MIT Asset Agent registry (run in Supabase SQL Editor)
-- Compatible with existing mit_heartbeats (agents also write heartbeats for PWA presence).

create extension if not exists pgcrypto;

create table if not exists public.mit_agents (
  id uuid primary key default gen_random_uuid(),
  workspace_id text not null default 'main',
  agent_id text not null,
  asset_id text,
  asset_tag text,
  hostname text,
  serial_number text,
  mac_address text,
  device_fingerprint text not null,
  manufacturer text,
  model text,
  ip_address text,
  status text not null default 'active'
    check (status in ('active', 'disabled', 'pending')),
  agent_version text,
  last_seen timestamptz,
  token_hash text,
  token_revoked boolean not null default false,
  system_info jsonb not null default '{}'::jsonb,
  installed_at timestamptz not null default now(),
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  unique (workspace_id, agent_id),
  unique (workspace_id, device_fingerprint)
);

create index if not exists mit_agents_workspace_last_seen_idx
  on public.mit_agents (workspace_id, last_seen desc);

create index if not exists mit_agents_serial_idx
  on public.mit_agents (workspace_id, serial_number);

create index if not exists mit_agents_mac_idx
  on public.mit_agents (workspace_id, mac_address);

alter table public.mit_agents enable row level security;

-- PWA (anon): read + admin updates (status / revoke flags). Never expose token_hash to clients if avoidable —
-- select policies allow admin UI; do not grant INSERT to anon (registration is Edge Function only).

drop policy if exists "mit_agents_anon_select" on public.mit_agents;
create policy "mit_agents_anon_select"
  on public.mit_agents
  for select
  to anon, authenticated
  using (true);

drop policy if exists "mit_agents_anon_update" on public.mit_agents;
create policy "mit_agents_anon_update"
  on public.mit_agents
  for update
  to anon, authenticated
  using (true)
  with check (true);

drop policy if exists "mit_agents_anon_delete" on public.mit_agents;
create policy "mit_agents_anon_delete"
  on public.mit_agents
  for delete
  to anon, authenticated
  using (true);

-- No anon INSERT — register-agent Edge Function uses service role.

comment on table public.mit_agents is 'Per-device MIT Asset Agent registry; token_hash only, never plaintext tokens.';
