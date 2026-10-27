FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY src/UAI.ZeroVP.DTO/UAI.ZeroVP.DTO.csproj src/UAI.ZeroVP.DTO/
COPY src/UAI.ZeroVP.Data/UAI.ZeroVP.Data.csproj src/UAI.ZeroVP.Data/
COPY src/UAI.ZeroVP.API/UAI.ZeroVP.API.csproj src/UAI.ZeroVP.API/

RUN dotnet restore src/UAI.ZeroVP.API/UAI.ZeroVP.API.csproj

COPY . .

RUN dotnet publish src/UAI.ZeroVP.API/UAI.ZeroVP.API.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false


FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/data /app/logs \
    && chown -R $APP_UID /app/data /app/logs

COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080

EXPOSE 8080

USER $APP_UID

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl --fail --silent --show-error http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "UAI.ZeroVP.API.dll"]