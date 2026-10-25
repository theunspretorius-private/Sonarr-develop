# Local development image. Build and run with:
#   podman build -t sonarr-local .
#   podman run -d --name sonarr -p 8989:8989 -v sonarr-config:/config sonarr-local

FROM docker.io/library/node:24-bookworm-slim AS frontend
WORKDIR /build
COPY package.json yarn.lock .yarnrc ./
RUN yarn install --frozen-lockfile --network-timeout 300000
COPY tsconfig.json vite.config.ts index.html ./
COPY frontend ./frontend
COPY src/NzbDrone.Core/Localization ./src/NzbDrone.Core/Localization
RUN yarn build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
WORKDIR /build
COPY Logo ./Logo
COPY src ./src
RUN dotnet publish src/NzbDrone.Console/Sonarr.Console.csproj \
      -c Release -f net10.0 -r linux-x64 --self-contained \
      -p:Platform=Posix -p:EnableAnalyzers=false -p:TreatWarningsAsErrors=false \
 && dotnet publish src/NzbDrone.Mono/Sonarr.Mono.csproj \
      -c Release -f net10.0 -r linux-x64 --self-contained \
      -p:Platform=Posix -p:EnableAnalyzers=false -p:TreatWarningsAsErrors=false

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0
RUN apt-get update \
 && apt-get install -y --no-install-recommends libsqlite3-0 libmediainfo0v5 tzdata \
 && rm -rf /var/lib/apt/lists/*
COPY --from=backend /build/_output/net10.0/linux-x64/publish /app
COPY --from=frontend /build/_output/UI /app/UI
VOLUME /config
EXPOSE 8989
ENTRYPOINT ["/app/Sonarr", "-nobrowser", "-data=/config"]
