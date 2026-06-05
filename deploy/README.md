# Deploying swarm-volume-sync (#1 tracer)

> **Status: HITL.** This slice's acceptance criteria can only be verified on a
> real multi-node Docker Swarm. The steps below are the human-in-the-loop
> verification procedure. The agent's decision logic is unit-tested in
> `tests/SwarmVolumeSync.Core.Tests`; what needs a live cluster is the Docker
> socket access, rsync-over-SSH transport, secret wiring, and the overlay net.

## 1. Generate the shared SSH keypair (swarm secrets)

```sh
ssh-keygen -t ed25519 -N "" -f svs_key
docker secret create svs_ssh_key    svs_key
docker secret create svs_ssh_pubkey svs_key.pub
rm svs_key svs_key.pub
```

## 2. Build and distribute the image

Single-registry swarms:

```sh
docker build -t <registry>/swarm-volume-sync:latest .
docker push <registry>/swarm-volume-sync:latest
```

Then set `image:` in `stack.yml` accordingly. (No registry? `docker build` on
every node, keeping the `swarm-volume-sync:latest` tag.)

## 3. Create the metadata directory on every node

The agent bind-mounts a host path (`SVS_METADATA_DIR`, default
`/var/lib/swarm-volume-sync`) to persist per-node version metadata **outside**
the volumes root. Swarm validates bind-mount sources before a task starts and
does **not** create them, so this directory must exist on every node first —
including any node that later joins:

```sh
sudo mkdir -p /var/lib/swarm-volume-sync
```

Skip it on a node and the agent task there is rejected with
`invalid mount config ... bind source path does not exist`, never runs, and that
node is silently left uncovered — exactly the failover gap this service exists
to close. (The Docker socket and volumes-root mounts already exist on every
node, so only this one needs creating.)

## 4. Deploy as a global service

```sh
docker stack deploy -c deploy/stack.yml svs
```

If you change the stack name, update `SVS_SERVICE_NAME` to `<stack>_agent` so
`tasks.<name>` resolves the full mesh.

## 5. Verify the tracer (acceptance criteria)

On node A:

```sh
docker volume create --label swarm-volume-sync.enable=true appdata
docker run -d --name writer -v appdata:/data alpine \
  sh -c 'echo hello-from-A > /data/marker && sleep 3600'
```

Within one poll interval, on node B:

```sh
cat /var/lib/docker/volumes/appdata/_data/marker   # => hello-from-A
```

Seeing the marker on every other node confirms: global deploy, docker.sock
source detection, `tasks.` peer discovery, and rsync-over-SSH transport.

## Caveats (see CONTEXT.md)

- Best-effort, crash-consistent replication. **Do not replicate database
  volumes** — use native DB HA.
- Versioning (#3) and the pull-before-serve guard (#5) are in place: a node
  scheduled onto an empty volume hydrates from the highest-versioned peer before
  it may push, and `rsync --delete` is only used by a confirmed source that has
  won the version check (ADR-0003). Change-driven sync (#4), observability (#6),
  retention-based reclaim + split-brain handling (#7), and backfill of
  freshly-joined nodes (#8) are all implemented.

## Opting in an existing volume

In the default `labelled` mode, only volumes carrying `swarm-volume-sync.enable=true`
are replicated. Docker volume labels are **immutable**, so an already-populated
volume cannot be opted in by relabelling it. Clone it into a new, labelled
volume instead (run on the node that holds the data — the source is mounted
read-only and left untouched):

```sh
./deploy/clone-volume.sh <source-volume> [dest-volume]   # dest defaults to <source>-svs
```

Then repoint the service/stack to the new volume. The agent tracks it within one
poll interval (`curl localhost:47654/status`). Alternatively, switch the agent
to `all` mode (`SVS_SYNC_MODE: all`) to replicate every named-local volume in
place — except those carrying `swarm-volume-sync.ignore=true`.

## Observability

- `GET :47654/status` — per-volume source, version, holders, sync lag, coverage.
- `GET :47654/metrics` — Prometheus (`svs_volume_coverage`, `svs_sync_lag_seconds`,
  `svs_last_sync_timestamp`).
- WARN logs name any **under-replicated** volume (coverage < 100%) — your
  failover-risk early warning. Watch these before relying on HA.
