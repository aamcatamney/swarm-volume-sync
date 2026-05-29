# Best-effort async replication, not distributed storage

**Status:** accepted

## Context & decision

The goal is true HA for Swarm services with local volumes: every node that
Swarm might fail a service onto should already hold a copy of its volume. The
obvious "correct" solution is synchronous distributed/replicated storage
(Ceph, GlusterFS, Longhorn, DRBD), which can guarantee zero data loss on
failover.

We deliberately chose **asynchronous, best-effort replication** instead: a
lightweight global service that rsyncs volume copies between nodes on a short
interval. A failover is guaranteed to *find a copy* on the target node, but
that copy may be stale by up to the sync interval — writes made between the
last sync and a crash are lost.

## Why

- Distributed storage is heavy to run, operate, and reason about; it changes
  the storage substrate of the whole cluster.
- A large class of HA workloads (configs, uploaded files, journaled app state)
  tolerate a small loss window and only need "a recent copy exists somewhere."
- Async replication is cheap, simple, and drop-in over existing `local`
  volumes with no storage-layer change.

## Consequences

- We must never market or document this as zero-loss. "Best-effort" is a
  first-class term (see CONTEXT.md).
- Databases are explicitly out of scope for safe use (torn live copies) —
  they should use native replication. See ADR-0003 context and the
  crash-consistency caveat in CONTEXT.md.
- If a user genuinely needs zero-loss, the answer is distributed storage, not
  this service — and we should say so rather than stretch this design.
