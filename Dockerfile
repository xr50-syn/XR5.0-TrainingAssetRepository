# The test project targets .NET 10, while the application still targets .NET 8.
# Use the newer SDK to support both target frameworks during container builds.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build-env
WORKDIR /App

# Copy everything
COPY . ./
# Restore as distinct layers
RUN dotnet restore
# Build and publish a release
# RUN dotnet publish -c Release -o out




# RUN dotnet ef migrations add InitialCreate
# RUN dotnet ef database update

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0

# run-migrations.sh invokes `dotnet ef` and `dotnet run`, so the final image also
# needs an SDK. Keep the ASP.NET 8 base above for the net8.0 application runtime,
# and layer in the .NET 10 SDK from the official build image for current tooling.
COPY --from=build-env /usr/share/dotnet /usr/share/dotnet

RUN dotnet tool install --global dotnet-ef --version 8.0.8

# Ensure the PATH includes .NET global tools
ENV PATH="$PATH:/root/.dotnet/tools"

WORKDIR /App
COPY --from=build-env /App .

# Copy the migration script
COPY run-migrations.sh .

# Make the script executable
RUN chmod +x run-migrations.sh
RUN apt-get update && apt-get install -y curl default-mysql-client jq

# Health check for Docker
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
    CMD curl -f http://localhost:5286/health || exit 1

# ENTRYPOINT ["dotnet", "XR50TrainingAssetRepo.dll"]
ENTRYPOINT ["./run-migrations.sh"]
