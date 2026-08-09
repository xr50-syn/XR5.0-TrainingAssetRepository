#!/bin/bash
set -e

echo "Waiting for database to be ready..."

# Wait for MySQL to be ready
until mysql -h mariadb -u${XR50_REPO_DB_USER} -p${XR50_REPO_DB_PASSWORD} -e "SELECT 1;" > /dev/null 2>&1; do
    echo "MySQL is unavailable - sleeping"
    sleep 5
done

echo "MariaDB is up - running migrations..."

MIGRATION_NAME="InitialCreate"
MIGRATION_PATTERN="./Migrations/*${MIGRATION_NAME}.cs"

# Check if migrations directory exists
if [ ! -f $MIGRATION_PATTERN ]; then
  echo "Migrations directory not found. Generating initial migration..."
  dotnet ef migrations add InitialCreate --context XR50TrainingContext
fi

# Get raw output from dotnet ef migrations list
RAW_OUTPUT=$(dotnet ef migrations list --json)

# Filter out non-JSON lines and capture only the JSON block
JSON_OUTPUT=$(echo "$RAW_OUTPUT" | sed -n '/^\[/,/\]$/p')


# Check if JSON is valid and not empty
if echo "$JSON_OUTPUT" | jq empty 2>/dev/null; then
  # Count unapplied migrations (case-insensitive comparison for 'Applied')
  UNAPPLIED_MIGRATIONS=$(echo "$JSON_OUTPUT" | jq '[.[] | select(.["Applied" | ascii_downcase] == false)] | length')
  

  if [ "$UNAPPLIED_MIGRATIONS" -gt 0 ]; then
    echo "Unapplied Migrations Count: $UNAPPLIED_MIGRATIONS, applying..."
    dotnet ef database update
  else
    echo "No unapplied migrations found. Database is up to date."
  fi
else
  echo "Error: Invalid JSON output from 'dotnet ef migrations list --json'."
  exit 1
fi




# Start the application.
# --no-launch-profile is required: without it `dotnet run` applies Properties/launchSettings.json,
# whose profiles pin ASPNETCORE_ENVIRONMENT=Development, and launch-profile variables take
# precedence over the ambient environment. The container would then always run as Development
# regardless of ASPNETCORE_ENVIRONMENT, keeping the anonymous authorization bypass, the
# Development-only JWT bearer scheme and Swagger enabled in every deployment.
echo "Starting the application..."
dotnet run --no-launch-profile
