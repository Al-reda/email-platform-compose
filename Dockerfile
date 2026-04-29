# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY src/Shared/Shared.csproj                  src/Shared/
COPY src/Compose.Api/Compose.Api.csproj        src/Compose.Api/
RUN dotnet restore src/Compose.Api/Compose.Api.csproj

COPY src/Shared/          src/Shared/
COPY src/Compose.Api/     src/Compose.Api/

RUN dotnet publish src/Compose.Api/Compose.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "EmailPlatform.Compose.Api.dll"]
