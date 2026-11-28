# syntax=docker/dockerfile:1

# ---- Build ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore only what the runtime project needs (better layer caching).
COPY ["SEI-RJ-Monitor.sln", "./"]
COPY ["src/Domain/SeiRjMonitor.Domain.csproj", "src/Domain/"]
COPY ["src/Application/SeiRjMonitor.Application.csproj", "src/Application/"]
COPY ["src/InfraStructure/SeiRjMonitor.Infrastructure.csproj", "src/InfraStructure/"]
COPY ["src/CrossCutting/SeiRjMonitor.CrossCutting.csproj", "src/CrossCutting/"]
COPY ["src/Presentation/SeiRjMonitor.Presentation.csproj", "src/Presentation/"]
RUN dotnet restore "src/Presentation/SeiRjMonitor.Presentation.csproj"

COPY . .
RUN dotnet publish "src/Presentation/SeiRjMonitor.Presentation.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# ---- Runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# curl is used only by the container health check.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_NOLOGO=1

COPY --from=build --chown=app:app /app/publish .

# The .NET 8 image ships an unprivileged "app" user (UID 1654).
USER app

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD curl -fsS "http://127.0.0.1:${PORT:-8080}/health/live" || exit 1

# Render injeta PORT; localmente/Docker Compose cai no 8080.
ENTRYPOINT ["sh", "-c", "exec dotnet SeiRjMonitor.Presentation.dll --urls http://+:${PORT:-8080}"]
