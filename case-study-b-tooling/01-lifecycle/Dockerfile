# ============================================================================
#  Case study B, example 1 - the baseline Dockerfile for this whole case study
#
#  Multi-stage build with an unprivileged account and a health check.
#  We do not dwell on the individual instructions here - case study B is about tooling.
#  For a line-by-line walkthrough of the Dockerfile see case study A.
#
#  The APP_VERSION argument is used in example 4 (tags and registry).
# ============================================================================

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY UAI.ZeroVP.sln .
COPY src/UAI.ZeroVP.DTO/UAI.ZeroVP.DTO.csproj   src/UAI.ZeroVP.DTO/
COPY src/UAI.ZeroVP.Data/UAI.ZeroVP.Data.csproj src/UAI.ZeroVP.Data/
COPY src/UAI.ZeroVP.API/UAI.ZeroVP.API.csproj   src/UAI.ZeroVP.API/
RUN dotnet restore UAI.ZeroVP.sln

COPY . .

# ARG is a variable that exists ONLY during the build (unlike ENV).
# It is passed in with "docker build --build-arg APP_VERSION=1.2.3".
ARG APP_VERSION=1.0.0

RUN dotnet publish src/UAI.ZeroVP.API/UAI.ZeroVP.API.csproj \
        --configuration Release \
        --no-restore \
        --output /app \
        -p:Version=${APP_VERSION}

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

ARG APP_VERSION=1.0.0
LABEL org.opencontainers.image.title="UAI.ZeroVP API" \
      org.opencontainers.image.version="${APP_VERSION}"

RUN apt-get update \
 && apt-get install --yes --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

HEALTHCHECK --interval=15s --timeout=3s --start-period=20s --retries=3 \
    CMD curl --fail --silent http://localhost:8080/health || exit 1

USER $APP_UID
ENTRYPOINT ["dotnet", "UAI.ZeroVP.API.dll"]
