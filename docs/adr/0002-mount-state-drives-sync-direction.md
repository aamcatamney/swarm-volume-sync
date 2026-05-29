# Mount state drives sync direction

**Status:** accepted

## Context & decision

In a full-mesh topology every node holds a copy of a volume, so the copy's mere
existence cannot tell us which node is authoritative. We need a rule for which
node is the **source** (pushes) and which are **replicas** (receive only);
getting it wrong overwrites live data with stale, and letting two nodes write
causes split-brain corruption.

Decision: **the source is the node whose running container currently mounts the
volume**, determined by querying the local Docker daemon. The live writer is
definitionally the node running the service. Every other node is a passive
replica.

## Considered options

- **mtime / last-writer-wins** — rejected: clock skew and concurrent writes
  cause silent corruption; no reliable notion of "authoritative."
- **Manual primary annotation** — rejected: the primary is exactly the thing
  that fails in HA, so a static label defeats the purpose.
- **Mount-follows (chosen)** — uses Swarm's own scheduling as ground truth; the
  authoritative node is wherever the container actually runs.

## Consequences

- Each agent needs the local Docker socket to detect local mounts.
- During failover there is a brief ambiguous window (old container dying, new
  starting). Acceptable under best-effort: worst case is one redundant stale
  sync, corrected on the next cycle and guarded by versioning (ADR-0003).
