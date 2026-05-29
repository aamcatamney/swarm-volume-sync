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

## 3. Deploy as a global service

```sh
docker stack deploy -c deploy/stack.yml svs
```

If you change the stack name, update `SVS_SERVICE_NAME` to `<stack>_agent` so
`tasks.<name>` resolves the full mesh.

## 4. Verify the tracer (acceptance criteria)

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
- No versioning / pull-before-serve guard yet (#3, #5). Until #5 lands, a node
  scheduled onto an empty volume could push emptiness; this tracer deliberately
  does **not** use `rsync --delete`, which limits (not eliminates) the blast
  radius. Do not run this in production before #5.
