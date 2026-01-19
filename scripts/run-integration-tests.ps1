# Integration Test Runner for XR5.0 Training Asset Repository
# Spins up Docker infrastructure, runs tests, and tears down
#
# Usage:
#   .\scripts\run-integration-tests.ps1 [level]
#
# Levels:
#   1 - Smoke tests only (fast, ~30 seconds)
#   2 - Smoke + functional tests (comprehensive, ~2-5 minutes)
#
# Examples:
#   .\scripts\run-integration-tests.ps1 1    # Quick smoke tests
#   .\scripts\run-integration-tests.ps1 2    # Full functional tests
#   .\scripts\run-integration-tests.ps1      # Defaults to level 1

param(
    [Parameter(Position=0)]
    [ValidateSet("1", "2")]
    [string]$TestLevel = "1"
)

$ErrorActionPreference = "Stop"

# Configuration
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent $ScriptDir
$ComposeFile = Join-Path $ProjectRoot "docker-compose.yaml"
$EnvFile = Join-Path $ProjectRoot ".env.minio"
$Profile = "sandbox"
$ApiUrl = "http://localhost:5286"
$HealthEndpoint = "$ApiUrl/health"
$MaxWaitSeconds = 120

# Colors
function Write-Info { Write-Host "[INFO] $args" -ForegroundColor Blue }
function Write-Success { Write-Host "[SUCCESS] $args" -ForegroundColor Green }
function Write-Warning { Write-Host "[WARNING] $args" -ForegroundColor Yellow }
function Write-Error { Write-Host "[ERROR] $args" -ForegroundColor Red }
function Write-Step { Write-Host "`n==> $args" -ForegroundColor Yellow }

# Cleanup function
function Invoke-Cleanup {
    Write-Step "Cleaning up..."
    Set-Location $ProjectRoot
    docker compose --profile $Profile down --volumes --remove-orphans 2>$null
    Write-Info "Cleanup complete"
}

# Wait for service to be healthy
function Wait-ForHealth {
    param(
        [string]$Url,
        [int]$MaxAttempts
    )

    Write-Info "Waiting for service at $Url (max ${MaxAttempts}s)..."

    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        try {
            $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 5 -ErrorAction SilentlyContinue
            if ($response.StatusCode -eq 200) {
                Write-Success "Service is healthy!"
                return $true
            }
        } catch {
            # Service not ready yet
        }
        Write-Host "." -NoNewline
        Start-Sleep -Seconds 1
    }

    Write-Host ""
    Write-Error "Service failed to become healthy after $MaxAttempts seconds"
    return $false
}

# Wait for database
function Wait-ForDatabase {
    $maxAttempts = 60

    Write-Info "Waiting for MariaDB to be ready..."

    for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
        $result = docker compose exec -T mariadb mysqladmin ping -h localhost -u root --password=root_password 2>$null
        if ($result -match "alive") {
            Write-Success "MariaDB is ready!"
            return $true
        }
        Write-Host "." -NoNewline
        Start-Sleep -Seconds 1
    }

    Write-Host ""
    Write-Error "MariaDB failed to become ready after $maxAttempts seconds"
    return $false
}

# Run smoke tests
function Invoke-SmokeTests {
    Write-Step "Running Smoke Tests..."

    $testTenant = "test-integration-$(Get-Date -Format 'yyyyMMddHHmmss')"
    $failed = $false

    # Test 1: Health endpoint
    Write-Info "Testing health endpoint..."
    try {
        $response = Invoke-WebRequest -Uri "$ApiUrl/health" -UseBasicParsing -TimeoutSec 10
        Write-Success "Health check passed"
    } catch {
        Write-Error "Health check failed"
        $failed = $true
    }

    # Test 2: Swagger UI
    Write-Info "Testing Swagger UI..."
    try {
        $response = Invoke-WebRequest -Uri "$ApiUrl/swagger/index.html" -UseBasicParsing -TimeoutSec 10
        Write-Success "Swagger UI accessible"
    } catch {
        Write-Error "Swagger UI not accessible"
        $failed = $true
    }

    # Test 3: Create tenant
    Write-Info "Testing tenant creation..."
    try {
        $body = @{
            name = $testTenant
            displayName = "Integration Test Tenant"
        } | ConvertTo-Json

        $response = Invoke-WebRequest -Uri "$ApiUrl/api/tenants" `
            -Method Post `
            -Body $body `
            -ContentType "application/json" `
            -UseBasicParsing `
            -TimeoutSec 30

        Write-Success "Tenant created successfully"

        # Test 4: Get tenant
        Write-Info "Testing tenant retrieval..."
        try {
            $response = Invoke-WebRequest -Uri "$ApiUrl/api/tenants/$testTenant" -UseBasicParsing -TimeoutSec 10
            Write-Success "Tenant retrieval successful"
        } catch {
            Write-Warning "Tenant retrieval failed"
        }

        # Test 5: Materials endpoint
        Write-Info "Testing materials endpoint..."
        try {
            $response = Invoke-WebRequest -Uri "$ApiUrl/api/$testTenant/materials" -UseBasicParsing -TimeoutSec 10
            Write-Success "Materials endpoint accessible"
        } catch {
            Write-Warning "Materials endpoint not accessible"
        }

        # Test 6: Chat health endpoint
        Write-Info "Testing chat health endpoint..."
        try {
            $response = Invoke-WebRequest -Uri "$ApiUrl/api/$testTenant/chat/health" -UseBasicParsing -TimeoutSec 10
            Write-Success "Chat health endpoint accessible"
        } catch {
            Write-Warning "Chat health endpoint not accessible (expected if no chatbot configured)"
        }

        # Test 7: Voice assistant health endpoint
        Write-Info "Testing voice assistant health endpoint..."
        try {
            $response = Invoke-WebRequest -Uri "$ApiUrl/api/$testTenant/voice-assistant/health" -UseBasicParsing -TimeoutSec 10
            Write-Success "Voice assistant health endpoint accessible"
        } catch {
            Write-Warning "Voice assistant health endpoint not accessible (expected if no API configured)"
        }

        # Cleanup: Delete tenant
        Write-Info "Cleaning up test tenant..."
        try {
            Invoke-WebRequest -Uri "$ApiUrl/api/tenants/$testTenant" -Method Delete -UseBasicParsing -TimeoutSec 10 2>$null
        } catch {
            # Ignore cleanup errors
        }

    } catch {
        Write-Error "Tenant creation failed: $_"
        $failed = $true
    }

    return -not $failed
}

# Run functional tests
function Invoke-FunctionalTests {
    Write-Step "Running Functional Tests..."

    Set-Location (Join-Path $ProjectRoot "tests\functional")

    # Check if node_modules exists
    if (-not (Test-Path "node_modules")) {
        Write-Info "Installing test dependencies..."
        npm install
    }

    # Set the API URL for tests
    $env:XR50_API_URL = $ApiUrl

    # Run the tests
    Write-Info "Executing Jest test suites..."
    $result = npm test -- --passWithNoTests --forceExit 2>&1

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Some functional tests failed"
        Write-Host $result
        return $false
    }

    Write-Success "All functional tests passed!"
    return $true
}

# Main execution
function Main {
    Write-Host ""
    Write-Host "=============================================="
    Write-Host "  XR5.0 Integration Test Runner"
    Write-Host "  Test Level: $TestLevel"
    Write-Host "=============================================="
    Write-Host ""

    Set-Location $ProjectRoot

    try {
        # Step 1: Clean up any existing containers
        Write-Step "Stopping any existing containers..."
        docker compose --profile $Profile down --volumes --remove-orphans 2>$null

        # Step 2: Start infrastructure
        Write-Step "Starting Docker infrastructure (profile: $Profile)..."
        if (Test-Path $EnvFile) {
            docker compose --env-file $EnvFile --profile $Profile up -d
        } else {
            Write-Warning "Environment file $EnvFile not found, using defaults"
            docker compose --profile $Profile up -d
        }

        # Step 3: Wait for database
        if (-not (Wait-ForDatabase)) {
            throw "Database failed to start"
        }

        # Step 4: Wait for API to be healthy
        if (-not (Wait-ForHealth -Url $HealthEndpoint -MaxAttempts $MaxWaitSeconds)) {
            throw "API failed to become healthy"
        }

        # Give the app a few more seconds to fully initialize
        Write-Info "Waiting for application to fully initialize..."
        Start-Sleep -Seconds 5

        # Step 5: Run tests based on level
        $testResult = $true

        # Always run smoke tests
        if (-not (Invoke-SmokeTests)) {
            $testResult = $false
        }

        # Run functional tests if level 2
        if ($TestLevel -eq "2") {
            if ($testResult) {
                if (-not (Invoke-FunctionalTests)) {
                    $testResult = $false
                }
            } else {
                Write-Warning "Skipping functional tests due to smoke test failures"
            }
        }

        # Summary
        Write-Host ""
        Write-Host "=============================================="
        if ($testResult) {
            Write-Success "All tests passed!"
            Write-Host "=============================================="
        } else {
            Write-Error "Some tests failed!"
            Write-Host "=============================================="
            exit 1
        }
    }
    finally {
        # Always cleanup
        Invoke-Cleanup
    }
}

# Run main function
Main
