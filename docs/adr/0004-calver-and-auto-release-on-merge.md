# CalVer and auto-release-on-merge

**Status:** accepted

## Context & decision

The artifact this repo ships is a single multi-arch container image
(`ghcr.io/<owner>/swarm-volume-sync`). It is deployed, not consumed as a library
or API — nothing downstream pins a version range or depends on semver's
compatibility contract. There is also one maintainer and a trunk-based flow
(direct pushes to `main`). So the questions semver answers ("is this a breaking
change for a dependent?") have no consumer to answer them for.

Decision:

- **Versioning is CalVer `YYYY.M.MICRO`** (e.g. `2026.6.0`). `MICRO` starts at
  `0` each calendar month and increments per release within that month. The
  format is deliberately semver-parseable (no leading-zero month — `2026.6.0`,
  not `2026.06.0`) so existing semver-based image tagging
  (`latest` / `2026.6.0` / `2026.6` / `sha`) keeps working unchanged.
- **Releases are cut automatically on merge to `main`**, gated by a **paths
  filter** (`src/**`, `Dockerfile`, `*.slnx`, `**/*.csproj`). A docs-only or
  workflow-only push changes no bytes in the image, so it mints no version.
  Every CalVer tag therefore corresponds to a real binary change.
- **One workflow** computes the version, builds + pushes the image with the
  version injected as a build-arg, then creates the Git tag and GitHub Release
  (auto-generated notes + a `docker pull` block). It is a single workflow
  because a tag pushed by the default `GITHUB_TOKEN` does **not** trigger a
  second workflow, so a "release-tags / build-on-tag" split would silently never
  build.

## Why

- **No consumer = no semver contract.** CalVer communicates the one thing that
  matters for a deployable: how recent is this build. "When was it cut" beats a
  hand-assigned major/minor nobody contracts against.
- **Auto-on-merge keeps the latest `main` always released**, matching the
  trunk-based, single-maintainer flow — no separate "cut a release" ritual to
  forget. The paths filter stops that from turning into release-per-typo.
- **Single workflow** sidesteps the `GITHUB_TOKEN` no-retrigger rule without
  introducing a stored PAT (which would mean secret rotation and broader scope
  for a self-inflicted split), and lets the version be computed *before* the
  image build so it can be baked in cleanly (see [Agent version] in CONTEXT.md).

## Consequences

- **More releases than a manual cadence** — one per src-touching merge. Several
  same-month merges produce `2026.6.0`, `2026.6.1`, … This is intended; the tag
  stream is a build log, not a curated changelog.
- **Version is computed in CI, not stored in source.** The next `MICRO` is
  derived from existing Git tags for the current `YYYY.M` prefix. The tag list
  is the source of truth; a force-deleted tag could cause a collision.
- **No human gate on what ships.** Anything merged to `main` that touches the
  filtered paths is released and pushed to GHCR. Pre-merge CI (tests) is the
  only gate; there is no staging tag.
- **Format is locked by the semver-compatibility constraint.** Switching to a
  leading-zero month or dropping `MICRO` later would break the existing
  `type=semver` image tagging and any pins on `2026.6`.

[Agent version]: ../../CONTEXT.md
