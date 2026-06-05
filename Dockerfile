# syntax=docker/dockerfile:1

# --- build ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY SwarmVolumeSync.slnx ./
COPY src/SwarmVolumeSync.Core/SwarmVolumeSync.Core.csproj src/SwarmVolumeSync.Core/
COPY src/SwarmVolumeSync.Agent/SwarmVolumeSync.Agent.csproj src/SwarmVolumeSync.Agent/
RUN dotnet restore src/SwarmVolumeSync.Agent/SwarmVolumeSync.Agent.csproj
COPY . .
# Agent version (CONTEXT.md): CalVer minted in CI, baked in here. Dev builds
# default to 0.0.0-dev. COMMIT is appended to AssemblyInformationalVersion as
# +<sha>, which the agent splits back out at runtime.
ARG VERSION=0.0.0-dev
ARG COMMIT=dev
RUN dotnet publish src/SwarmVolumeSync.Agent/SwarmVolumeSync.Agent.csproj -c Release -o /app \
    -p:Version=${VERSION} -p:SourceRevisionId=${COMMIT}

# --- runtime ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# rsync = byte transport; openssh = secure channel; curl = healthcheck
RUN apt-get update \
    && apt-get install -y --no-install-recommends rsync openssh-server curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app ./
COPY deploy/entrypoint.sh /usr/local/bin/entrypoint.sh
RUN chmod +x /usr/local/bin/entrypoint.sh
# 22 = rsync-over-ssh transport; 47654 = control API (versions, status, metrics)
EXPOSE 22 47654
HEALTHCHECK --interval=30s --timeout=5s --retries=3 \
    CMD curl -fsS http://localhost:47654/healthz || exit 1
ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
