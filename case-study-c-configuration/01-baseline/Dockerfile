# ============================================================================
#  Case study C, example 1 - the baseline Dockerfile for this whole case study
#
#  Multi-stage build, unprivileged account, health check.
#  Case study C is not about building images but about getting settings into them.
#  For a line-by-line walkthrough see case study A.
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
        --configuration Release --no-restore --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

RUN apt-get update \
 && apt-get install --yes --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .

# Default settings baked into the image. Everything below can be overridden
# when the container starts - which is what example 2 is about.
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

HEALTHCHECK --interval=15s --timeout=3s --start-period=20s --retries=3 \
    CMD curl --fail --silent http://localhost:8080/health || exit 1

USER $APP_UID
ENTRYPOINT ["dotnet", "UAI.ZeroVP.API.dll"]
