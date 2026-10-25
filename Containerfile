# Build and run locally:
#   podman build -t sonarr-local .
#   podman run -d --name sonarr -p 8989:8989 -v sonarr-config:/config sonarr-local
#
# Cross-build for an ARM64 host (e.g. a NAS):
#   podman build --platform linux/arm64 -t sonarr-local:arm64 .

FROM --platform=$BUILDPLATFORM docker.io/library/node:24-bookworm-slim AS frontend
WORKDIR /build
COPY package.json yarn.lock .yarnrc ./
RUN yarn install --frozen-lockfile --network-timeout 300000
COPY tsconfig.json vite.config.ts index.html ./
COPY frontend ./frontend
COPY src/NzbDrone.Core/Localization ./src/NzbDrone.Core/Localization
RUN yarn build

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
ARG TARGETARCH
WORKDIR /build
COPY Logo ./Logo
COPY src ./src
RUN ARCH="${TARGETARCH:-$(dpkg --print-architecture)}" \
 && case "$ARCH" in amd64) RID=linux-x64 ;; arm64) RID=linux-arm64 ;; *) echo "Unsupported arch: $ARCH" && exit 1 ;; esac \
 && for project in NzbDrone.Console/Sonarr.Console.csproj NzbDrone.Mono/Sonarr.Mono.csproj; do \
      dotnet publish "src/$project" -c Release -f net10.0 -r "$RID" --self-contained -o /build/publish \
        -p:Platform=Posix -p:EnableAnalyzers=false -p:TreatWarningsAsErrors=false || exit 1; \
    done

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0
RUN apt-get update \
 && apt-get install -y --no-install-recommends libsqlite3-0 libmediainfo0v5 tzdata \
 && rm -rf /var/lib/apt/lists/*
COPY --from=backend /build/publish /app/bin
COPY --from=frontend /build/_output/UI /app/bin/UI
# Sonarr only reads package_info when it runs from a folder named "bin".
# UpdateMethod=Docker stops the built-in updater replacing this custom build.
RUN printf 'UpdateMethod=Docker\nBranch=main\nPackageAuthor=local build\n' > /app/package_info
VOLUME /config
EXPOSE 8989
ENTRYPOINT ["/app/bin/Sonarr", "-nobrowser", "-data=/config"]
