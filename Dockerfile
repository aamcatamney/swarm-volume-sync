# syntax=docker/dockerfile:1

# --- build ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY SwarmVolumeSync.slnx ./
COPY src/SwarmVolumeSync.Core/SwarmVolumeSync.Core.csproj src/SwarmVolumeSync.Core/
COPY src/SwarmVolumeSync.Agent/SwarmVolumeSync.Agent.csproj src/SwarmVolumeSync.Agent/
RUN dotnet restore src/SwarmVolumeSync.Agent/SwarmVolumeSync.Agent.csproj
COPY . .
RUN dotnet publish src/SwarmVolumeSync.Agent/SwarmVolumeSync.Agent.csproj -c Release -o /app

# --- runtime ---
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
# rsync = byte transport; openssh = secure channel between agents over the overlay net
RUN apt-get update \
    && apt-get install -y --no-install-recommends rsync openssh-server \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app ./
COPY deploy/entrypoint.sh /usr/local/bin/entrypoint.sh
RUN chmod +x /usr/local/bin/entrypoint.sh
EXPOSE 22
ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
