#!/bin/sh
set -e

# Agents talk to each other over SSH on the overlay network using a single
# shared keypair distributed as swarm secrets:
#   svs_ssh_key     - private key (used by rsync to push)
#   svs_ssh_pubkey  - public key  (placed in authorized_keys to receive)

mkdir -p /root/.ssh /run/sshd
chmod 700 /root/.ssh

if [ -f /run/secrets/svs_ssh_pubkey ]; then
  cp /run/secrets/svs_ssh_pubkey /root/.ssh/authorized_keys
  chmod 600 /root/.ssh/authorized_keys
fi

# Secrets mount read-only with permissions ssh rejects; copy to a private path.
if [ -f /run/secrets/svs_ssh_key ]; then
  cp /run/secrets/svs_ssh_key /root/.ssh/id_svs
  chmod 600 /root/.ssh/id_svs
fi

# Host keys for sshd.
ssh-keygen -A >/dev/null 2>&1 || true

# Allow key-based root login only.
{
  echo "PermitRootLogin prohibit-password"
  echo "PasswordAuthentication no"
  echo "PubkeyAuthentication yes"
} > /etc/ssh/sshd_config.d/svs.conf

/usr/sbin/sshd

exec dotnet /app/SwarmVolumeSync.Agent.dll
