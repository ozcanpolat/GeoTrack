# GeoTrack web uygulaması (çok aşamalı derleme). Derleme bağlamı: depo kökü.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Önce yalnızca proje dosyası: paket geri yüklemesi katman önbelleğinden yararlanır.
COPY GeoTrack.Web/GeoTrack.Web.csproj GeoTrack.Web/
RUN dotnet restore GeoTrack.Web/GeoTrack.Web.csproj

COPY GeoTrack.Web/ GeoTrack.Web/
RUN dotnet publish GeoTrack.Web/GeoTrack.Web.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Health check için ASP.NET runtime imajına küçük bir HTTP istemcisi eklenir.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && useradd --system --uid 10001 --create-home appuser \
    && mkdir -p /home/appuser/.aspnet/DataProtection-Keys /home/appuser/photos \
    && chown -R appuser:appuser /home/appuser/.aspnet /home/appuser/photos

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER appuser
ENTRYPOINT ["dotnet", "GeoTrack.Web.dll"]
