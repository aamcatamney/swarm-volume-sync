# swarm-volume-sync

A global Docker Swarm service that replicates local Docker volumes across all
nodes, so that when Swarm reschedules a service onto a different node, the
volume data is **already there**. The goal is practical high availability for
stateful services backed by `local` volumes.

> **Best-effort, not zero-loss.** Replication is asynchronous: a failover is
> guaranteed to *find a copy* of the volume on the target node, but that copy
> may be stale by up to the sync interval. Writes made between the last sync and
> a crash are lost. If you need zero data loss, use synchronous distributed
> storage (Ceph, GlusterFS, Longhorn, DRBD) instead — see
> [`docs/adr/0001`](docs/adr/0001-best-effort-async-replication-not-distributed-storage.md).
>
> **Crash-consistent only.** Copies are taken from live volumes, so they are
> equivalent to a power-loss snapshot. This is fine for configs, uploaded files,
> and journaled app state — but **not safe for databases**. Replicate database
> volumes with the database's own HA, not this service.

## Quick start

Using the prebuilt image from GitHub Container Registry — no local build needed.
Run on a swarm manager node:

```sh
# 0. Have a swarm (skip if already initialised)
docker swarm init

# 1. Create the shared SSH keypair as swarm secrets (on the manager)
ssh-keygen -t ed25519 -N "" -f svs_key
docker secret create svs_ssh_key    svs_key
docker secret create svs_ssh_pubkey svs_key.pub
rm svs_key svs_key.pub

# 2. Create the metadata dir on EVERY node (Swarm bind mounts won't auto-create it)
sudo mkdir -p /var/lib/swarm-volume-sync

# 3. Deploy the global service (pulls ghcr.io/aamcatamney/swarm-volume-sync:latest)
curl -O https://raw.githubusercontent.com/aamcatamney/swarm-volume-sync/main/deploy/stack.yml
docker stack deploy -c stack.yml svs

# 4. Opt a volume in to replication
docker volume create --label swarm-volume-sync.enable=true appdata

# 5. Check coverage from any node
curl localhost:47654/status
```

> **Run step 2 on every node**, and on any node that later joins the swarm.
> Swarm validates bind-mount sources before a task starts and does **not**
> create them, so an agent on a node missing `/var/lib/swarm-volume-sync` is
> rejected with `bind source path does not exist` and never runs there —
> silently leaving that node uncovered. (Steps 1 and 3 run on a manager only.)

### Example stack (agent + your app, one file)

A self-contained stack that runs the agent **and** an example app whose volume
is replicated. Save as `stack.yml`, then `docker stack deploy -c stack.yml demo`:

```yaml
version: "3.8"

services:
  # The volume-sync agent: one per node (global), full-mesh replication.
  svs-agent:
    image: ghcr.io/aamcatamney/swarm-volume-sync:latest
    deploy:
      mode: global
      restart_policy: { condition: any }
    environment:
      # MUST equal "<stack>_<service>" so tasks.<name> resolves the mesh.
      SVS_SERVICE_NAME: demo_svs-agent
      SVS_SYNC_MODE: labelled            # only volumes with the enable label
      SVS_SSH_KEY_PATH: /root/.ssh/id_svs
    ports:
      - { target: 47654, published: 47654, mode: host } # control API per node
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock:ro
      - /var/lib/docker/volumes:/var/lib/docker/volumes
      - /var/lib/swarm-volume-sync:/var/lib/swarm-volume-sync
    secrets: [svs_ssh_key, svs_ssh_pubkey]
    networks: [svs]

  # Your stateful app. Its volume carries the enable label, so it is replicated
  # to every node — wherever Swarm reschedules this task, the data is already there.
  app:
    image: nginx:alpine
    volumes:
      - appdata:/usr/share/nginx/html
    deploy:
      replicas: 1
    networks: [svs]

volumes:
  appdata:
    labels:
      swarm-volume-sync.enable: "true"   # opt this volume in

networks:
  svs:
    driver: overlay

secrets:                                  # created once: see step 1 above
  svs_ssh_key:    { external: true }
  svs_ssh_pubkey: { external: true }
```

> Remember the `SVS_SERVICE_NAME` rule: it must be `<stack>_<service>`. Deployed
> as stack `demo` with service `svs-agent`, that is `demo_svs-agent`.

> The GHCR package must be pullable by every node. If it is private, run
> `docker login ghcr.io` on each node, or make the package public (repo →
> Packages → package settings → visibility). For a full multi-node walkthrough
> and the end-to-end verification steps, see [`deploy/README.md`](deploy/README.md).

## What it does

- Runs one agent per node (a Swarm **global service**).
- Watches `local` named volumes and replicates them to every other node
  (**full mesh**), so any node Swarm picks for failover already has a copy.
- Replicates **all** volumes or only those carrying a label — your choice.
- The node whose running container currently mounts a volume is its **source**;
  every other node holds a passive **replica**. Direction follows the mount, so
  the live writer is always authoritative
  ([`docs/adr/0002`](docs/adr/0002-mount-state-drives-sync-direction.md)).
- Guards against the classic failover wipe: a node that comes up with an empty
  volume **hydrates from the highest-versioned peer before it may push**, so it
  can never overwrite good copies
  ([`docs/adr/0003`](docs/adr/0003-versioned-volumes-and-pull-before-serve.md)).
- Surfaces **under-replicated** volumes (coverage < 100%) loudly, so you see
  failover risk *before* an outage.

For the full domain vocabulary, see [`CONTEXT.md`](CONTEXT.md).

## How it works

```
            ┌──────────── node A (source) ────────────┐
            │  app container  ──mounts──►  appdata vol │
            │  svs agent                               │
            │   • detects mount via docker.sock        │
            │   • versions volume by content checksum  │
            │   • rsync -a (+--delete) over SSH ───────┼──┐
            └──────────────────────────────────────────┘  │   overlay network
            ┌──────────── node B (replica) ───────────┐    │
            │  svs agent  ◄── rsync ───────────────────┼────┘
            │   • holds copy at /var/lib/docker/volumes │
            │   • control API reports its version ◄─────┼── version checks,
            └──────────────────────────────────────────┘   metadata, /status
```

Each cycle an agent: reads Docker facts (which volumes exist, which are mounted
locally), discovers peers via the `tasks.<service>` overlay DNS name, versions
each volume it sources by a content checksum, and `rsync`s the bytes to every
peer over SSH. A small HTTP **control API** on the overlay network handles
coordination (version checks, metadata propagation, status, metrics); SSH/rsync
is used only for the bulk byte transfer.

Triggering is change-driven: a filesystem watch (inotify) fires a sync after a
short debounce, with a slow safety-net poll catching anything missed.

## Tech stack

- **.NET 10 / C#** — two projects:
  - `SwarmVolumeSync.Core` — pure domain logic (versioning, selection,
    pull-before-serve, retention, coverage, backfill planning), fully unit-tested
    with **xUnit** (56 tests).
  - `SwarmVolumeSync.Agent` — an **ASP.NET** host running the control API plus a
    `BackgroundService` sync loop. Talks to the Docker daemon via
    **Docker.DotNet**.
- **rsync over SSH** — delta byte transport between nodes.
- **Docker Swarm** — global service, overlay networking, secrets.
- **Prometheus** text exposition for metrics.

## Deploy

> The procedure below is summarised; see [`deploy/README.md`](deploy/README.md)
> for the full version including the end-to-end verification steps.

### 1. Generate the shared SSH keypair as swarm secrets

```sh
ssh-keygen -t ed25519 -N "" -f svs_key
docker secret create svs_ssh_key    svs_key
docker secret create svs_ssh_pubkey svs_key.pub
rm svs_key svs_key.pub
```

### 2. Get the image

A multi-arch image (`linux/amd64`, `linux/arm64`) is built and published to
GitHub Container Registry by CI. Every image-touching push to `main` cuts a
[CalVer](docs/adr/0004-calver-and-auto-release-on-merge.md) release
`YYYY.M.MICRO` (e.g. `2026.6.0`) with a matching GitHub Release, and pushes the
tags `latest`, `2026.6.0`, `2026.6`, and `sha-<commit>` (see
[`.github/workflows/release.yml`](.github/workflows/release.yml)):

```sh
docker pull ghcr.io/aamcatamney/swarm-volume-sync:latest   # or pin :2026.6.0
```

The running agent reports its release as `agentVersion` on `GET /status` and the
`svs_build_info{version,commit}` Prometheus gauge — handy for watching a rolling
deploy across nodes.

Set `image: ghcr.io/aamcatamney/swarm-volume-sync:latest` in `deploy/stack.yml`.
(If the GHCR package is private, `docker login ghcr.io` first, or make the
package public in the repo's Packages settings so nodes can pull it freely.)

Prefer to build locally instead?

```sh
docker build -t <registry>/swarm-volume-sync:latest . && docker push <registry>/swarm-volume-sync:latest
# or, with no registry, build on every node:
docker build -t swarm-volume-sync:latest .
```

### 3. Deploy as a global service

```sh
docker stack deploy -c deploy/stack.yml svs
```

The stack name (`svs`) determines the service name (`svs_agent`) and therefore
the discovery DNS name (`tasks.svs_agent`). If you change the stack name, update
`SVS_SERVICE_NAME` to `<stack>_agent` to match.

### 4. Use it

Label a volume to opt it in (default mode is opt-in):

```sh
docker volume create --label swarm-volume-sync.enable=true appdata
```

Within a sync interval the volume's data appears under
`/var/lib/docker/volumes/appdata/_data/` on every node.

## Configuration

All configuration is via environment variables (set in `deploy/stack.yml`):

| Variable | Default | Purpose |
|---|---|---|
| `SVS_SERVICE_NAME` | `swarm-volume-sync` | Must equal `<stack>_<service>` so `tasks.<name>` resolves the mesh |
| `SVS_SYNC_MODE` | `labelled` | `labelled` (opt-in) or `all` |
| `SVS_ENABLE_LABEL` | `swarm-volume-sync.enable` | Label key that opts a volume in (value must be truthy) |
| `SVS_IGNORE_LABEL` | `swarm-volume-sync.ignore` | Label that always excludes a volume, even in `all` mode |
| `SVS_SSH_KEY_PATH` | `/run/secrets/svs_ssh_key` | Private key used by rsync (the stack copies it to `/root/.ssh/id_svs`) |
| `SVS_DOCKER_SOCKET` | `unix:///var/run/docker.sock` | Local Docker daemon |
| `SVS_VOLUMES_ROOT` | `/var/lib/docker/volumes` | Where volume data lives on the host |
| `SVS_METADATA_DIR` | `/var/lib/swarm-volume-sync` | Per-node version metadata (kept outside volumes) |
| `SVS_CONTROL_API_PORT` | `47654` | HTTP control API / metrics port |
| `SVS_DEBOUNCE_SECONDS` | `5` | Coalesce a burst of writes before syncing |
| `SVS_SAFETY_POLL_SECONDS` | `300` | Safety-net poll catching missed change events |
| `SVS_RETENTION_DAYS` | `7` | Reclaim an orphaned copy only after this long with no source in the mesh |
| `SVS_BACKFILL_BWLIMIT_KB` | `0` | rsync `--bwlimit` for a joining node's backfill (`0` = unlimited) |
| `SVS_BACKFILL_CONCURRENCY` | `2` | Max concurrent volume pulls during backfill |

### Required privileges

Each agent mounts (see `deploy/stack.yml`):

- `/var/run/docker.sock` (read-only) — to detect local mounts, list volumes, and
  read its own node identity.
- `/var/lib/docker/volumes` — to read/write actual volume data for rsync.
- `/var/lib/swarm-volume-sync` — to persist version metadata across restarts.

## Observability

- `GET :47654/status` — per volume: source, version, holders + their versions,
  sync lag, coverage.
- `GET :47654/metrics` — Prometheus gauges: `svs_volume_coverage`,
  `svs_sync_lag_seconds`, `svs_last_sync_timestamp`.
- `GET :47654/healthz` — agent liveness (used by the container healthcheck).
- **WARN logs** name any under-replicated volume — your failover-risk early
  warning. Watch these before relying on HA.

## Development

```sh
dotnet build          # build everything
dotnet test           # run the Core unit tests
```

`SwarmVolumeSync.Core` holds the decision logic and is exercised entirely by unit
tests; the agent project wires that logic to Docker, rsync, and HTTP.

### Run a single agent locally

For smoke-testing the agent and control API without a swarm, use the root
`docker-compose.yml` (single node, no peers — nothing replicates across nodes,
but the agent runs and serves the API):

```sh
mkdir -p deploy/secrets
ssh-keygen -t ed25519 -N "" -f deploy/secrets/svs_ssh_key
mv deploy/secrets/svs_ssh_key.pub deploy/secrets/svs_ssh_pubkey
docker compose up --build
# then: curl localhost:47654/status
```

Production deployment is always the Swarm stack (`deploy/stack.yml`), not Compose
— global mode and overlay peer discovery require Swarm.
