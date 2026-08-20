# .NET 10 LTS for build and runtime.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore as its own layer so dependency downloads are cached across source changes.
COPY XR50TrainingAssetRepo.csproj ./
RUN dotnet restore XR50TrainingAssetRepo.csproj

COPY . .
RUN dotnet publish XR50TrainingAssetRepo.csproj -c Release -o /app/publish --no-restore

# Runtime image: no SDK, no EF tools. The committed migrations are compiled into the
# application and applied by it (at startup, or through the `migrate` verb).
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# The aspnet image listens on 8080 by default; the stack, the health checks and the docs use 5286.
ENV ASPNETCORE_URLS=http://+:5286

# start_period covers schema migration of every tenant database at startup.
HEALTHCHECK --interval=30s --timeout=10s --start-period=90s --retries=3 \
    CMD curl -f http://localhost:5286/health || exit 1

# The published DLL ignores Properties/launchSettings.json, so ASPNETCORE_ENVIRONMENT from the
# environment is honoured. `docker compose run --rm --no-deps training-repo migrate --status`
# runs the schema migrator on its own.
ENTRYPOINT ["dotnet", "XR50TrainingAssetRepo.dll"]
