# syntax=docker/dockerfile:1
# HTN Admin API: build with the .NET SDK, run on the slim ASP.NET runtime as a non-root user.
# Base images can be swapped (for mirrors or a pinned digest) with --build-arg SDK_IMAGE / RUNTIME_IMAGE.
ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0
ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0

# ---- manifests: only the files that affect `dotnet restore` --------------------------------
# Copies every *.csproj / props / solution file with its folder structure, however many projects
# are added later. The next stage's COPY --from is content-addressed, so the restore layer stays
# cached until a project/package file really changes.
FROM ${SDK_IMAGE} AS manifests
WORKDIR /src
COPY . .
RUN mkdir /manifests \
 && find . -type f \( -name '*.sln' -o -name '*.slnx' -o -name '*.csproj' \
      -o -name 'Directory.Build.props' -o -name 'Directory.Build.targets' -o -name 'Directory.Packages.props' \
      -o -name 'global.json' -o -iname 'nuget.config' -o -name 'packages.lock.json' \) \
      -exec cp --parents {} /manifests/ \;

# ---- build: restore (cached) then publish Htn.Api --------------------------------------------
FROM ${SDK_IMAGE} AS build
WORKDIR /src
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1
COPY --from=manifests /manifests/ ./
RUN --mount=type=cache,id=htn-nuget,target=/root/.nuget/packages \
    dotnet restore src/Htn.Api/Htn.Api.csproj
COPY src/ src/
RUN --mount=type=cache,id=htn-nuget,target=/root/.nuget/packages \
    dotnet publish src/Htn.Api/Htn.Api.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

# ---- final: runtime only -------------------------------------------------------------------
FROM ${RUNTIME_IMAGE} AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_HTTP_PORTS= \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
# Files stay root-owned (read-only for the app user); the process runs as the built-in `app` user.
COPY --from=build /app/publish ./
USER $APP_UID
EXPOSE 8080
# The aspnet image ships no curl/wget. Rather than adding packages (size + CVE surface), bash's
# /dev/tcp sends a real HTTP request and requires a "200" status line from /health.
HEALTHCHECK --interval=15s --timeout=5s --start-period=30s --retries=5 \
  CMD ["bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /health HTTP/1.1\\r\\nHost: localhost\\r\\nConnection: close\\r\\n\\r\\n' >&3 && head -n1 <&3 | grep -q ' 200 '"]
ENTRYPOINT ["dotnet", "Htn.Api.dll"]
