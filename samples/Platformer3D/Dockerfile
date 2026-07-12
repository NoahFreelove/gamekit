# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 GameKit contributors
#
# Multi-stage Dockerfile for Platformer3D demo.
# Build context must be the REPO ROOT (not samples/Platformer3D/) so the
# SDK stage can COPY the src/GameKit.* project references that the sample
# depends on via ProjectReference.
#
# Build from repo root:
#   docker build -f samples/Platformer3D/Dockerfile -t platformer3d:latest .
#
# Or via compose (preferred — compose sets context automatically):
#   docker compose -f samples/Platformer3D/docker-compose.yml build
#
# DEMO ONLY: RSA keys are generated fresh each docker build and baked into the
# image. These keys are for the offline demo only — do NOT use in production.
# Production deployments must inject keys via Docker secrets or environment
# variable key material.

# ---------------------------------------------------------------------------
# Stage 1: SDK build
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy the csproj files first so restore is layer-cached independently of source.
COPY ["samples/Platformer3D/Platformer3D.csproj", "samples/Platformer3D/"]
COPY ["samples/Platformer3D.GameServer/Platformer3D.GameServer.csproj", "samples/Platformer3D.GameServer/"]

# Copy all GameKit source packages that Platformer3D references by ProjectReference.
COPY ["src/GameKit.Core/GameKit.Core.csproj", "src/GameKit.Core/"]
COPY ["src/GameKit.Auth/GameKit.Auth.csproj", "src/GameKit.Auth/"]
COPY ["src/GameKit.Rankings/GameKit.Rankings.csproj", "src/GameKit.Rankings/"]
COPY ["src/GameKit.Matchmaking/GameKit.Matchmaking.csproj", "src/GameKit.Matchmaking/"]
COPY ["src/GameKit.Lobby/GameKit.Lobby.csproj", "src/GameKit.Lobby/"]
COPY ["src/GameKit.Presence/GameKit.Presence.csproj", "src/GameKit.Presence/"]
COPY ["src/GameKit.OpenApi/GameKit.OpenApi.csproj", "src/GameKit.OpenApi/"]
COPY ["src/GameKit.Admin.UI/GameKit.Admin.UI.csproj", "src/GameKit.Admin.UI/"]

# Repo-root MSBuild props (TargetFramework, Nullable, etc.) and SDK version pin.
COPY ["Directory.Build.props", "./"]
COPY ["Directory.Packages.props", "./"]
COPY ["global.json", "./"]

# Restore with NuGetAudit=false to skip the pre-existing MessagePack NU1903 advisory
# (pre-existing transitive dep, not introduced by this demo — see project memory).
RUN dotnet restore "samples/Platformer3D/Platformer3D.csproj" -p:NuGetAudit=false

# Copy the full source tree after the restore cache layer.
COPY . .

# Publish release build. The restore pass above populated the NuGet cache; publish
# includes the vendored wwwroot/ (static files are copied by the .NET publish
# pipeline via UseStaticWebAssets / web SDK defaults).
# Note: --no-restore is intentionally omitted here — the SDK resolves a fresh
# restore against the warm NuGet cache to handle any package-version mismatches
# between the minimal-csproj restore stage and the full source tree.
RUN dotnet publish "samples/Platformer3D/Platformer3D.csproj" \
    -c Release \
    -o /app/publish \
    -p:NuGetAudit=false

# ---------------------------------------------------------------------------
# Stage 2: ASP.NET Core runtime
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

# Install curl for the /health/ready healthcheck (the aspnet:10.0 base image
# does not ship curl by default; wget is also absent on Debian slim variants).
# Also install openssl for demo RSA key generation.
RUN apt-get update && \
    apt-get install -y --no-install-recommends curl openssl && \
    rm -rf /var/lib/apt/lists/*

# DEMO ONLY: Generate a fresh RSA-2048 key pair at build time.
# These keys are NOT secrets — they are regenerated every docker build and are
# clearly demo-only. Production: inject real key material via Docker secrets or
# environment-variable PEM content (override PrivateKeyPemPath / PublicKeyPemPath).
RUN mkdir -p /app/keys && \
    openssl genrsa -out /app/keys/private.pem 2048 && \
    openssl rsa -in /app/keys/private.pem -pubout -out /app/keys/public.pem && \
    echo "# DEMO ONLY — do not use these keys in production" > /app/keys/KEYS_ARE_DEMO_ONLY.txt

EXPOSE 8080

ENTRYPOINT ["dotnet", "Platformer3D.dll"]
