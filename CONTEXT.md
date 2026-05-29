# Context: swarm-volume-sync

A global Docker Swarm service that replicates Docker volumes across nodes so a
service's volume data already exists on whatever node Swarm fails it over to.

## Glossary

### Best-effort replication
The replication model. Each node holds a **recent** copy of a replicated
volume, not a write-synchronous one. Because replication is asynchronous, there
is always a lag window: writes made on the source node between the last sync and
a crash are lost on failover.

We do **not** promise zero data loss. A failover move is *guaranteed to find a
copy* of the volume on the target node; that copy may be stale by up to the sync
interval. Zero-loss would require synchronous distributed storage (Ceph,
GlusterFS, Longhorn, DRBD) — explicitly out of scope.

### Replicated volume
A Docker volume that this service keeps copied across nodes. Selection is
configurable: all volumes, or only those carrying a configured label.

Scope is restricted to **named volumes using the `local` driver** — they live
at a known path (`/var/lib/docker/volumes/<name>/_data`) and carry Docker
labels (which the label filter needs). Bind mounts (no labels, host-specific
paths), tmpfs (ephemeral), and networked volume drivers (already shared) are
out of scope.

### Full mesh
The replication topology (v1). Every replicated volume is copied to **every
node** in the swarm. Wasteful on disk (N nodes = N copies) but the only
topology that truly guarantees a copy exists wherever Swarm reschedules a
service. Constraint-aware / N-replica topologies are deferred.

### Source (of a volume)
The node currently authoritative for a replicated volume — the one whose
**running container mounts it**. Determined by querying the local Docker daemon,
not by config or file mtimes. The source pushes; every other node is a passive
**replica** that only accepts pushes. This makes "the live writer" definitional
(the node running the service) and avoids split-brain from last-writer-wins.

### Replica (of a volume)
Any node holding a copy of a volume it is not currently the source for. Accepts
pushes; never writes to its own copy. A replica becomes the source if/when Swarm
schedules the service's container onto it.

### Agent
The per-node unit of this service. Deployed as a Docker Swarm **global service**
(one task per node). The agent is a C# process that *orchestrates* replication —
decides which volumes to sync, in which direction, to which nodes — and shells
out to `rsync` over SSH for the actual byte transfer. Each agent both pushes (as
source) and receives (as replica, via an sshd in the container). SSH keys are
distributed as a swarm secret.

### Sync trigger
How the source decides when to push. **Filesystem-watch (inotify) with a debounce**
is primary: on change, wait for activity to settle (default 5s) then push.
A slow **safety-net poll** (default 5min) backstops missed inotify events and
discovers new/removed volumes. Both intervals are configurable. The debounce
sets the practical lower bound of the loss window.

### Selection mode
Which volumes get replicated. Two modes via `SYNC_MODE`:
- `labelled` (**default**, opt-in): only volumes carrying the enable label.
- `all`: every named-local volume, except those explicitly opted out.

The enable label key is configurable (default `swarm-volume-sync.enable=true`).
An `swarm-volume-sync.ignore=true` label always excludes a volume, even in
`all` mode. Opt-in is the default because `all` would otherwise flood every node
with throwaway data (build caches, etc.) on first deploy.

### Volume version
A monotonic counter (a **generation**) attached to each replicated volume,
incremented as the source records new state. Every copy on every node carries
the version of the data it holds. Versioning exists to defeat the
empty-source-wipes-everyone failure: a node that becomes source with a *lower*
version than the mesh must not push.

### Pull-before-serve (hydrate)
The rule that protects against stale/empty sources. When a node becomes source
for a volume, **before pushing anything** it checks the mesh for a higher
version. If one exists, it pulls that copy first (hydrates), adopts that
version, and only then resumes pushing. Guarantees an empty freshly-created
volume (version 0) can never overwrite good data — its version loses, so it
pulls instead of pushes. Delete-propagation (`rsync --delete`) is only ever
applied by a source that has won the version check.

### Volume metadata
Per-node bookkeeping for each replicated volume: `{version, lastSyncedAt,
checksum, sourceNode}`. Stored in an agent-owned directory **outside** the
volume (e.g. `/var/lib/swarm-volume-sync/<vol>.meta`) so user volume bytes stay
pristine — the service never writes into `_data`.

### Control API
A small HTTP API each agent exposes on the swarm overlay network (e.g.
`GET /volumes/{name}/version`, health). Used for cheap metadata queries between
agents (the version check) and observability. **SSH/rsync is for bulk byte
transfer only; the control API is for coordination.**

### Retention-based reclaim
How replica bytes are removed — never by propagation. The service **never**
mesh-deletes a volume in response to a single `docker volume rm`, a removed
label, or an added `ignore`. Instead the volume is marked `orphaned` in
metadata and its bytes are reclaimed only after a configurable **retention
window** (default 7d) during which no source is seen. Deletion is the only
irreversible op, so the design biases hard toward keeping bytes; disk reclaim is
slow, explicit, and never a side-effect of a label change or one `rm`.

### Split-brain
Two nodes simultaneously sourcing the same volume (e.g. network partition with a
container running on each side). Each increments its version independently. On
heal, **higher version wins** and a loud warning is logged. This is an accepted
limitation of best-effort replication, not something the service prevents —
Swarm itself is expected to keep a replicated service to a single running task.

### Peer discovery
How agents find each other for the mesh. The swarm overlay **DNS name
`tasks.<service>`** enumerates all live agent task IPs (auto-updates on node
join/leave) — used as the target list for the control API and rsync. Each agent
reads its **own** node identity, local volumes, and local container mounts from
the **local Docker socket** (`/var/run/docker.sock`, mounted into every agent);
it never needs manager API access. The socket mount is a required privilege.

### Coverage / under-replication
The key health signal. A volume's **coverage** = nodes holding a copy ÷ total
nodes. A volume with coverage < 100% is **under-replicated** — failover onto a
node lacking a copy might fail, the exact risk this service exists to remove.
Under-replication is surfaced loudly (WARN logs) *before* an outage. Observed
via the control API `GET /status` (per volume: source, version, holders + their
versions, sync lag, coverage) and Prometheus `/metrics`
(`svs_volume_coverage`, `svs_sync_lag_seconds`, `svs_last_sync_timestamp`).

### Backfill
How a freshly-joined node reaches full coverage. The new agent queries peers'
`/status`, then pulls each selected volume from its current source (or the
highest-version holder if no live source) — the same pull-before-serve code
path. Backfill runs in the **background, bandwidth-limited** (`rsync --bwlimit`,
configurable) with a **concurrency cap** (K volumes at once) so a joining node
neither saturates the network nor starves live syncs nor stampedes one source.
The node reports under-replicated until caught up.

### Crash-consistency (and the database caveat)
Copies are made by rsyncing **live** volumes, so a replica holds a
crash-consistent snapshot — equivalent to a power-loss at copy time, not a clean
point-in-time. This is fine for journaled/recoverable workloads (configs,
uploaded files, app state) but **unsafe for databases** (Postgres, MySQL,
SQLite, etc.), where a torn copy may not recover. v1 guidance: **do not
replicate database volumes with this service — use the database's own
replication/HA.** Snapshot-then-sync (btrfs/ZFS/LVM) and app-quiesce hooks
(fsfreeze / pre-post-sync) are deferred opt-in enhancements.
