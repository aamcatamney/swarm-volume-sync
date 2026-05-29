# Versioned volumes and pull-before-serve

**Status:** accepted

## Context & decision

Mount-driven direction (ADR-0002) has a catastrophic failure mode: when Swarm
schedules a service onto a node that has no (or a stale) copy, Docker creates an
**empty** volume, the container mounts it, and that node instantly becomes the
"source" — then pushes its empty volume to every replica. With `rsync --delete`
this wipes good data cluster-wide. Total loss, caused by the very mechanism
meant to prevent loss.

Decision: attach a **monotonic version (generation)** to every volume copy, and
require **pull-before-serve**: when a node becomes source, before pushing it
checks the mesh (via the control API) for a higher version. If one exists it
**pulls that first**, adopts the version, and only then resumes pushing.
Delete-propagation is only ever performed by a source that has won the version
check.

## Why

- An empty fresh volume has version 0; it loses the version check and pulls
  instead of pushing, so it can never overwrite good data.
- A pure byte-sync (no metadata) cannot distinguish "legitimately emptied" from
  "freshly created empty" — versioning makes intent explicit.

## Consequences

- Introduces a metadata layer: each agent stores `{version, lastSyncedAt,
  checksum, sourceNode}` per volume, **outside** the volume (never in `_data`),
  queried between agents over the HTTP control API.
- Adds a coordination round-trip (version check) before any push.
- Does not solve true split-brain (two simultaneous sources across a
  partition); that remains higher-version-wins + warn, an accepted best-effort
  limitation.
