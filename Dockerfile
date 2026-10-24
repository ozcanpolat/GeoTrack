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
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "GeoTrack.Web.dll"]
