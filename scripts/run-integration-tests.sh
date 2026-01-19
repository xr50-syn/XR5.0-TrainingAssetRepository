#!/bin/bash
# Integration Test Runner for XR5.0 Training Asset Repository
# Spins up Docker infrastructure, runs tests, and tears down
#
# Usage:
#   ./scripts/run-integration-tests.sh [level]
#
# Levels:
#   1 - Smoke tests only (fast, ~30 seconds)
#   2 - Smoke + functional tests (comprehensive, ~2-5 minutes)
#
# Examples:
#   ./scripts/run-integration-tests.sh 1    # Quick smoke tests
#   ./scripts/run-integration-tests.sh 2    # Full functional tests
#   ./scripts/run-integration-tests.sh      # Defaults to level 1

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Configuration
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
COMPOSE_FILE="$PROJECT_ROOT/docker-compose.yaml"
ENV_FILE="$PROJECT_ROOT/.env.minio"
PROFILE="sandbox"
API_URL="http://localhost:5286"
HEALTH_ENDPOINT="$API_URL/health"
MAX_WAIT_SECONDS=120
TEST_LEVEL="${1:-1}"

# Logging functions
log_info() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

log_success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

log_warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

log_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

log_step() {
    echo -e "\n${YELLOW}==>${NC} $1"
}

# Cleanup function
cleanup() {
    log_step "Cleaning up..."
    cd "$PROJECT_ROOT"
    docker compose --profile "$PROFILE" down --volumes --remove-orphans 2>/dev/null || true
    log_info "Cleanup complete"
}

# Trap to ensure cleanup on exit
trap cleanup EXIT

# Wait for service to be healthy
wait_for_health() {
    local url=$1
    local max_attempts=$2
    local attempt=1

    log_info "Waiting for service at $url (max ${max_attempts}s)..."

    while [ $attempt -le $max_attempts ]; do
        if curl -s -f "$url" > /dev/null 2>&1; then
            log_success "Service is healthy!"
            return 0
        fi
        echo -n "."
        sleep 1
        attempt=$((attempt + 1))
    done

    echo ""
    log_error "Service failed to become healthy after ${max_attempts} seconds"
    return 1
}

# Wait for database to be ready
wait_for_database() {
    local max_attempts=60
    local attempt=1

    log_info "Waiting for MariaDB to be ready..."

    while [ $attempt -le $max_attempts ]; do
        if docker compose exec -T mariadb mysqladmin ping -h localhost -u root --password=root_password 2>/dev/null | grep -q "alive"; then
            log_success "MariaDB is ready!"
            return 0
        fi
        echo -n "."
        sleep 1
        attempt=$((attempt + 1))
    done

    echo ""
    log_error "MariaDB failed to become ready after ${max_attempts} seconds"
    return 1
}

# Run smoke tests
run_smoke_tests() {
    log_step "Running Smoke Tests..."

    local test_tenant="test-integration-$(date +%s)"
    local failed=0

    # Test 1: Health endpoint
    log_info "Testing health endpoint..."
    if curl -s -f "$API_URL/health" > /dev/null; then
        log_success "Health check passed"
    else
        log_error "Health check failed"
        failed=1
    fi

    # Test 2: Swagger UI
    log_info "Testing Swagger UI..."
    if curl -s -f "$API_URL/swagger/index.html" > /dev/null; then
        log_success "Swagger UI accessible"
    else
        log_error "Swagger UI not accessible"
        failed=1
    fi

    # Test 3: API test endpoint
    log_info "Testing API test endpoint..."
    if curl -s -f "$API_URL/api/test" > /dev/null; then
        log_success "API test endpoint accessible"
    else
        log_warning "API test endpoint not accessible (may be expected)"
    fi

    # Test 4: Create and delete tenant
    log_info "Testing tenant creation..."
    local create_response=$(curl -s -X POST "$API_URL/api/tenants" \
        -H "Content-Type: application/json" \
        -d "{\"name\": \"$test_tenant\", \"displayName\": \"Integration Test Tenant\"}" \
        -w "\n%{http_code}")
    local http_code=$(echo "$create_response" | tail -1)

    if [ "$http_code" = "200" ] || [ "$http_code" = "201" ]; then
        log_success "Tenant created successfully"

        # Test 5: Get tenant
        log_info "Testing tenant retrieval..."
        if curl -s -f "$API_URL/api/tenants/$test_tenant" > /dev/null; then
            log_success "Tenant retrieval successful"
        else
            log_warning "Tenant retrieval failed"
        fi

        # Test 6: Materials endpoint
        log_info "Testing materials endpoint..."
        if curl -s -f "$API_URL/api/$test_tenant/materials" > /dev/null; then
            log_success "Materials endpoint accessible"
        else
            log_warning "Materials endpoint not accessible"
        fi

        # Test 7: Chat endpoint (new)
        log_info "Testing chat health endpoint..."
        if curl -s "$API_URL/api/$test_tenant/chat/health" > /dev/null; then
            log_success "Chat health endpoint accessible"
        else
            log_warning "Chat health endpoint not accessible (expected if no chatbot configured)"
        fi

        # Test 8: Voice assistant endpoint (new)
        log_info "Testing voice assistant health endpoint..."
        if curl -s "$API_URL/api/$test_tenant/voice-assistant/health" > /dev/null; then
            log_success "Voice assistant health endpoint accessible"
        else
            log_warning "Voice assistant health endpoint not accessible (expected if no API configured)"
        fi

        # Cleanup: Delete tenant
        log_info "Cleaning up test tenant..."
        curl -s -X DELETE "$API_URL/api/tenants/$test_tenant" > /dev/null 2>&1 || true
    else
        log_error "Tenant creation failed with HTTP $http_code"
        failed=1
    fi

    return $failed
}

# Run functional tests (Node.js)
run_functional_tests() {
    log_step "Running Functional Tests..."

    cd "$PROJECT_ROOT/tests/functional"

    # Check if node_modules exists
    if [ ! -d "node_modules" ]; then
        log_info "Installing test dependencies..."
        npm install
    fi

    # Set the API URL for tests
    export XR50_API_URL="$API_URL"

    # Run the tests
    log_info "Executing Jest test suites..."
    npm test -- --passWithNoTests --forceExit 2>&1 || {
        log_error "Some functional tests failed"
        return 1
    }

    log_success "All functional tests passed!"
    return 0
}

# Main execution
main() {
    echo ""
    echo "=============================================="
    echo "  XR5.0 Integration Test Runner"
    echo "  Test Level: $TEST_LEVEL"
    echo "=============================================="
    echo ""

    # Validate test level
    if [ "$TEST_LEVEL" != "1" ] && [ "$TEST_LEVEL" != "2" ]; then
        log_error "Invalid test level: $TEST_LEVEL"
        echo "Usage: $0 [1|2]"
        echo "  1 - Smoke tests only"
        echo "  2 - Smoke + functional tests"
        exit 1
    fi

    cd "$PROJECT_ROOT"

    # Step 1: Clean up any existing containers
    log_step "Stopping any existing containers..."
    docker compose --profile "$PROFILE" down --volumes --remove-orphans 2>/dev/null || true

    # Step 2: Start infrastructure
    log_step "Starting Docker infrastructure (profile: $PROFILE)..."
    if [ -f "$ENV_FILE" ]; then
        docker compose --env-file "$ENV_FILE" --profile "$PROFILE" up -d
    else
        log_warning "Environment file $ENV_FILE not found, using defaults"
        docker compose --profile "$PROFILE" up -d
    fi

    # Step 3: Wait for database
    wait_for_database

    # Step 4: Wait for API to be healthy
    wait_for_health "$HEALTH_ENDPOINT" "$MAX_WAIT_SECONDS"

    # Give the app a few more seconds to fully initialize
    log_info "Waiting for application to fully initialize..."
    sleep 5

    # Step 5: Run tests based on level
    local test_result=0

    # Always run smoke tests
    run_smoke_tests || test_result=1

    # Run functional tests if level 2
    if [ "$TEST_LEVEL" = "2" ]; then
        if [ $test_result -eq 0 ]; then
            run_functional_tests || test_result=1
        else
            log_warning "Skipping functional tests due to smoke test failures"
        fi
    fi

    # Summary
    echo ""
    echo "=============================================="
    if [ $test_result -eq 0 ]; then
        log_success "All tests passed!"
        echo "=============================================="
        exit 0
    else
        log_error "Some tests failed!"
        echo "=============================================="
        exit 1
    fi
}

# Run main function
main
