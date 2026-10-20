# Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY apps/api/Carpoolr.Api.csproj apps/api/
RUN dotnet restore apps/api/Carpoolr.Api.csproj

COPY apps/api/ apps/api/
RUN dotnet publish apps/api/Carpoolr.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

USER $APP_UID

ENTRYPOINT ["dotnet", "Carpoolr.Api.dll"]