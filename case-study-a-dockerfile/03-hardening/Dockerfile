# ============================================================================
#  Case study A, example 3 - hardening the image
#
#  Built on example 2. What is new:
#   - runs as an unprivileged user instead of root
#   - HEALTHCHECK, so Docker itself watches whether the app is alive
#   - descriptive labels and default settings via ENV
# ============================================================================

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY UAI.ZeroVP.sln .
COPY src/UAI.ZeroVP.DTO/UAI.ZeroVP.DTO.csproj   src/UAI.ZeroVP.DTO/
COPY src/UAI.ZeroVP.Data/UAI.ZeroVP.Data.csproj src/UAI.ZeroVP.Data/
COPY src/UAI.ZeroVP.API/UAI.ZeroVP.API.csproj   src/UAI.ZeroVP.API/
RUN dotnet restore UAI.ZeroVP.sln

COPY . .
RUN dotnet publish src/UAI.ZeroVP.API/UAI.ZeroVP.API.csproj \
        --configuration Release \
        --no-restore \
        --output /app

# ---------------------------------------------------------------------------
#  STAGE 2 - runtime
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# LABEL attaches descriptive metadata to the image. The OCI convention keys
# are understood by registries and inventory tooling.
LABEL org.opencontainers.image.title="UAI.ZeroVP API" \
      org.opencontainers.image.description="Sample ToDo API for teaching Docker" \
      org.opencontainers.image.vendor="UAI" \
      org.opencontainers.image.licenses="MIT"

# The base image ships with neither curl nor wget, so HEALTHCHECK would have
# nothing to call /health with. We install curl - about 10 MB, a deliberate
# price for Docker being able to notice on its own when the app stops responding.
#
# Three things matter in this single RUN:
#   - update, install and cleanup must be ONE command, otherwise the package
#     index stays in the layer below and the image grows for nothing
#   - --no-install-recommends skips suggested but unneeded packages
#   - rm -rf /var/lib/apt/lists/* drops the downloaded package index
RUN apt-get update \
 && apt-get install --yes --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .

# ENV sets an environment variable that applies inside the container.
# Any of these can be overridden at "docker run" time with -e.
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=1

EXPOSE 8080

# HEALTHCHECK tells Docker how to verify the app inside is working.
# The result shows up in the STATUS column of "docker ps" as (healthy).
#   --interval     how often to check
#   --timeout      how long to wait for an answer
#   --start-period grace period during which a failure does not count
#   --retries      consecutive failures required before the container is unhealthy
HEALTHCHECK --interval=15s --timeout=3s --start-period=20s --retries=3 \
    CMD curl --fail --silent http://localhost:8080/health || exit 1

# USER switches the account the ENTRYPOINT will run under.
# Microsoft defines APP_UID (value 1654) in its own images for exactly this.
# Without this line the app would run as root - and if someone took it over
# through a bug in the code, they would own everything inside the container.
#
# This line must come AFTER apt-get, because installing packages needs root.
USER $APP_UID

ENTRYPOINT ["dotnet", "UAI.ZeroVP.API.dll"]
