#!/usr/bin/env bash
#
# clone-volume.sh — copy a local Docker volume into a NEW volume that carries
# the swarm-volume-sync enable label, so the agent starts replicating it.
#
# Why this exists: Docker volume labels are immutable (there is no
# `docker volume update`), so an already-populated volume cannot be opted in to
# label-based replication in place. This clones its data (source mounted
# read-only — never modified) into a freshly created, labelled volume.
#
# Run it ON THE NODE that currently holds the volume's data; volumes are
# node-local. Once the clone exists and is labelled, swarm-volume-sync picks it
# up within one poll interval and replicates it to the rest of the mesh.
#
# Usage:
#   ./clone-volume.sh <source-volume> [dest-volume]
#
#   dest-volume defaults to "<source-volume>-svs".
#
# Env overrides:
#   SVS_ENABLE_LABEL   enable label key   (default: swarm-volume-sync.enable)
#   HELPER_IMAGE       copy helper image  (default: alpine:3.20)
#
set -euo pipefail

ENABLE_LABEL_KEY="${SVS_ENABLE_LABEL:-swarm-volume-sync.enable}"
HELPER_IMAGE="${HELPER_IMAGE:-alpine:3.20}"

die() { printf 'error: %s\n' "$*" >&2; exit 1; }

usage() {
  sed -n '2,22p' "$0" | sed 's/^#\s\?//'
  exit "${1:-0}"
}

[[ "${1:-}" == "-h" || "${1:-}" == "--help" ]] && usage 0
[[ $# -ge 1 && $# -le 2 ]] || usage 1

src="$1"
dst="${2:-${src}-svs}"

command -v docker >/dev/null 2>&1 || die "docker not found on PATH"
[[ "$src" != "$dst" ]] || die "source and destination must differ"

# --- preflight --------------------------------------------------------------

docker volume inspect "$src" >/dev/null 2>&1 \
  || die "source volume '$src' does not exist on this node"

driver="$(docker volume inspect -f '{{ .Driver }}' "$src")"
[[ "$driver" == "local" ]] \
  || die "source volume '$src' uses driver '$driver'; only 'local' volumes are in scope"

if docker volume inspect "$dst" >/dev/null 2>&1; then
  die "destination volume '$dst' already exists — refusing to overwrite. Remove it or pick another name."
fi

printf 'Cloning local volume:\n  source: %s (read-only)\n  dest:   %s (label %s=true)\n\n' \
  "$src" "$dst" "$ENABLE_LABEL_KEY"

# --- create labelled destination -------------------------------------------
# Only the enable label is set. The source's com.docker.stack.namespace label
# is deliberately NOT copied: that marks a volume as stack-owned, and carrying
# it onto a hand-created volume can confuse `docker stack` ownership/pruning.

docker volume create --label "${ENABLE_LABEL_KEY}=true" "$dst" >/dev/null

# --- copy data --------------------------------------------------------------
# cp -a preserves permissions, ownership, timestamps, and symlinks. "/from/."
# copies the directory *contents* (including dotfiles) into the new volume.

docker run --rm \
  -v "$src":/from:ro \
  -v "$dst":/to \
  "$HELPER_IMAGE" \
  sh -c 'set -e; cp -a /from/. /to/ && sync'

# --- verify -----------------------------------------------------------------

read_bytes() { docker run --rm -v "$1":/v:ro "$HELPER_IMAGE" sh -c 'du -sb /v | cut -f1'; }
read_files() { docker run --rm -v "$1":/v:ro "$HELPER_IMAGE" sh -c 'find /v -mount | wc -l'; }

src_bytes="$(read_bytes "$src")"; dst_bytes="$(read_bytes "$dst")"
src_files="$(read_files "$src")"; dst_files="$(read_files "$dst")"

printf '\nVerification:\n  bytes  source=%s dest=%s\n  files  source=%s dest=%s\n' \
  "$src_bytes" "$dst_bytes" "$src_files" "$dst_files"

if [[ "$src_bytes" == "$dst_bytes" && "$src_files" == "$dst_files" ]]; then
  printf '  OK — byte and file counts match.\n'
else
  printf '  WARNING — counts differ. Inspect before relying on the clone.\n' >&2
fi

cat <<EOF

Done. New volume '$dst' is labelled for replication.

Next:
  1. Repoint the service/stack that used '$src' to mount '$dst'
     (or, to keep the original name, stop the service, remove '$src', recreate
      it WITH the label, and re-clone in the other direction).
  2. Within one poll interval, confirm it is tracked:
       curl localhost:47654/status   # '$dst' should appear, coverage rising

The source volume '$src' was not modified.
EOF
