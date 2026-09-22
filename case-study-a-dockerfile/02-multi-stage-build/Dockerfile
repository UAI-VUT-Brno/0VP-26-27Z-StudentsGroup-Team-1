# ============================================================================
#  Case study A, example 2 - multi-stage build and an ignore file
#
#  Two improvements over example 1:
#   1) The final image carries only the runtime, not the whole SDK.
#   2) Layers are ordered so that package restore comes from the cache.
# ============================================================================

# ---------------------------------------------------------------------------
#  STAGE 1 - build
#  Naming the stage (AS build) lets a later stage copy from its filesystem.
#  This stage never reaches the final image.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Project files first - not the whole source tree.
# Reason: Docker caches every layer and reuses it until its inputs change.
# The .csproj files change rarely, so the "dotnet restore" below is skipped
# on an ordinary code edit.
COPY UAI.ZeroVP.sln .
COPY src/UAI.ZeroVP.DTO/UAI.ZeroVP.DTO.csproj   src/UAI.ZeroVP.DTO/
COPY src/UAI.ZeroVP.Data/UAI.ZeroVP.Data.csproj src/UAI.ZeroVP.Data/
COPY src/UAI.ZeroVP.API/UAI.ZeroVP.API.csproj   src/UAI.ZeroVP.API/

# Download NuGet packages. This is the expensive layer, hence it sits high up.
RUN dotnet restore UAI.ZeroVP.sln

# Only now the rest of the source. A change to a .cs file invalidates this
# layer and everything below it, but not the package restore above it.
COPY . .

# Publish into /app. --no-restore because packages came from the layer above.
RUN dotnet publish src/UAI.ZeroVP.API/UAI.ZeroVP.API.csproj \
        --configuration Release \
        --no-restore \
        --output /app

# ---------------------------------------------------------------------------
#  STAGE 2 - runtime
#  A different, far smaller base image: it carries the ASP.NET Core runtime
#  but no compiler, no SDK and no source code.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# --from=build copies from the previous stage, not from the host disk.
# Only the published output crosses over - a few megabytes.
COPY --from=build /app .

EXPOSE 8080
ENTRYPOINT ["dotnet", "UAI.ZeroVP.API.dll"]
