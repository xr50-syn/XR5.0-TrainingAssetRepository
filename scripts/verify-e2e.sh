#!/bin/bash
# Verification ladder for the XR5.0 Training Asset Repository.
#
# Runs the checks from AGENTS.md "Verification" in order, cheapest first, and stops at the
# first failing rung so you diagnose one problem rather than a cascade. The narrative version,
# including how to write a targeted probe for a specific change, is in
# docs/guides/verification-workflow.md.
#
# Rungs:
#   build       dotnet build
#   hermetic    dotnet test (no infrastructure required)
#   functional  tests/functional Jest suites (requires a running stack)
#   authz       scripts/authz-probe.sh role matrix sweep (requires a running stack)
#
# Usage:
#   ./scripts/verify-e2e.sh                  # build + hermetic, then functional if the stack is up
#   ./scripts/verify-e2e.sh --up             # start the sandbox stack first, then run everything
#   ./scripts/verify-e2e.sh --rung hermetic  # one rung only
#   ./scripts/verify-e2e.sh --suite tenant   # functional rung, single suite (npm run test:tenant)
#   ./scripts/verify-e2e.sh --with-authz     # also run the authorization matrix sweep
#   ./scripts/verify-e2e.sh --down           # stop the stack and exit
#
# This script never drops volumes and never deletes tenants. Bringing the stack up with --up
# leaves it running so you can probe it afterwards; --down stops containers but keeps data.

set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT" || exit 1

API_URL="${API_URL:-http://localhost:5286}"
COMPOSE_PROFILE="${COMPOSE_PROFILE:-sandbox}"
TEST_PROJECT="tests/XR50TrainingAssetRepo.Tests/XR50TrainingAssetRepo.Tests.csproj"
HEALTH_TIMEOUT="${HEALTH_TIMEOUT:-180}"

GREEN='\033[0;32m'; RED='\033[0;31m'; YELLOW='\033[1;33m'; BLUE='\033[0;34m'; DIM='\033[2m'; NC='\033[0m'

RUNG=""; SUITE=""; DO_UP=0; DO_DOWN=0; WITH_AUTHZ=0
declare -a SUMMARY=()

# Print the header comment block (everything from line 2 up to the first non-comment line).
usage() { awk 'NR>1 { if (!/^#/) exit; sub(/^# ?/, ""); print }' "$0"; exit 0; }

while [ $# -gt 0 ]; do
    case "$1" in
        --up) DO_UP=1 ;;
        --down) DO_DOWN=1 ;;
        --with-authz) WITH_AUTHZ=1 ;;
        --rung) RUNG="${2:-}"; shift ;;
        --suite) SUITE="${2:-}"; shift ;;
        -h|--help) usage ;;
        *) echo -e "${RED}Unknown argument: $1${NC}" >&2; exit 2 ;;
    esac
    shift
done

step()   { echo -e "\n${BLUE}=== $* ===${NC}"; }
ok()     { echo -e "${GREEN}PASS${NC}  $*"; SUMMARY+=("PASS|$*"); }
bad()    { echo -e "${RED}FAIL${NC}  $*"; SUMMARY+=("FAIL|$*"); }
skip()   { echo -e "${YELLOW}SKIP${NC}  $*"; SUMMARY+=("SKIP|$*"); }
note()   { echo -e "${DIM}$*${NC}"; }

wants() { [ -z "$RUNG" ] || [ "$RUNG" = "$1" ]; }

api_health_code() { curl -s -m 5 -o /dev/null -w '%{http_code}' "$API_URL/health" 2>/dev/null; }

stack_is_up() { [ "$(api_health_code)" = "200" ]; }

wait_for_health() {
    local waited=0
    while [ "$waited" -lt "$HEALTH_TIMEOUT" ]; do
        stack_is_up && return 0
        sleep 3; waited=$((waited + 3))
        [ $((waited % 30)) -eq 0 ] && note "  still waiting for $API_URL/health (${waited}s)"
    done
    return 1
}

# The development anonymous bypass makes every authorization policy succeed, so a functional
# run against it reports green while proving nothing about auth. Refuse rather than mislead.
# Same gate as scripts/authz-probe.sh; see docs/guides/authorization-test-plan.md step P0-2.
anonymous_bypass_is_on() {
    local code
    code=$(curl -s -m 10 -o /dev/null -w '%{http_code}' "$API_URL/api/auth/me" 2>/dev/null)
    [ "$code" != "401" ] && [ "$code" != "000" ]
}

# --- stack lifecycle ----------------------------------------------------------------------

if [ "$DO_DOWN" -eq 1 ]; then
    step "Stopping the $COMPOSE_PROFILE stack (volumes preserved)"
    docker compose --profile "$COMPOSE_PROFILE" down
    exit $?
fi

if [ "$DO_UP" -eq 1 ]; then
    step "Starting the $COMPOSE_PROFILE stack"
    if ! docker compose --profile "$COMPOSE_PROFILE" up -d --build; then
        bad "docker compose up failed"; exit 1
    fi
    note "Waiting for $API_URL/health ..."
    if wait_for_health; then
        ok "stack healthy at $API_URL"
    else
        bad "stack did not become healthy within ${HEALTH_TIMEOUT}s"
        note "Inspect with: docker compose --profile $COMPOSE_PROFILE logs training-repo"
        exit 1
    fi
fi

# --- rung: build --------------------------------------------------------------------------

if wants build; then
    step "Build"
    if dotnet build --nologo -v quiet 2>&1 | tail -5; then
        ok "dotnet build"
    else
        bad "dotnet build"
        note "Stop here and fix the build before running anything else."
        exit 1
    fi
fi

# --- rung: hermetic -----------------------------------------------------------------------

if wants hermetic; then
    step "Hermetic tests"
    hermetic_out=$(dotnet test "$TEST_PROJECT" --nologo 2>&1)
    hermetic_rc=$?
    echo "$hermetic_out" | grep -E "^(Passed!|Failed!|\s*(Passed|Failed|Skipped|Total))" | tail -3
    if [ $hermetic_rc -eq 0 ]; then
        ok "$(echo "$hermetic_out" | grep -oE 'Failed: *[0-9]+, Passed: *[0-9]+[^-]*' | tail -1 | xargs)"
    else
        bad "hermetic tests"
        echo "$hermetic_out" | grep -E "error|\[FAIL\]" | head -20
        # A recursive copy of the test output into itself has broken this rung before; the fix
        # is to delete the gitignored build artifacts, not to edit any source.
        if echo "$hermetic_out" | grep -q "MSB3030"; then
            note "MSB3030 usually means nested test build artifacts. Try:"
            note "  rm -rf tests/XR50TrainingAssetRepo.Tests/bin tests/XR50TrainingAssetRepo.Tests/obj"
        fi
        exit 1
    fi
fi

# --- rung: functional ---------------------------------------------------------------------

if wants functional; then
    step "Functional tests"
    if ! stack_is_up; then
        skip "functional suite - no stack at $API_URL (start one with: $0 --up)"
        note "Reporting this as SKIPPED, not passed. Do not claim functional coverage without it."
    elif anonymous_bypass_is_on; then
        bad "functional suite - anonymous bypass appears to be ON"
        note "GET /api/auth/me did not return 401, so authorization policies are disabled and a"
        note "green run would prove nothing. Set IAM__AllowAnonymousInDevelopment=false and retry."
        exit 1
    else
        if [ ! -d tests/functional/node_modules ]; then
            note "Installing functional test dependencies ..."
            (cd tests/functional && npm install --silent) || { bad "npm install"; exit 1; }
        fi
        npm_target="test"
        [ -n "$SUITE" ] && npm_target="test:$SUITE"
        note "Running: npm run $npm_target (from tests/functional)"
        func_out=$(cd tests/functional && npm run "$npm_target" 2>&1)
        func_rc=$?
        echo "$func_out" | grep -E "^(Tests:|Test Suites:|Cleanup complete)" | tail -4
        if [ $func_rc -eq 0 ]; then
            ok "functional suite ($npm_target)"
        else
            bad "functional suite ($npm_target)"
            echo "$func_out" | grep -E "✕|●|failed" | head -20
            exit 1
        fi
        # The suites provision a tenant per run and delete it in teardown. A leftover means an
        # aborted run, which will skew the next one.
        if echo "$func_out" | grep -q "resources deleted, 0 failed"; then
            ok "functional teardown cleaned up its resources"
        else
            skip "could not confirm functional teardown cleanup - check for leftover test_* tenants"
        fi
    fi
fi

# --- rung: authz --------------------------------------------------------------------------

if [ "$WITH_AUTHZ" -eq 1 ] || [ "$RUNG" = "authz" ]; then
    step "Authorization matrix"
    if ! stack_is_up; then
        skip "authz probe - no stack at $API_URL"
    elif [ ! -x scripts/authz-probe.sh ]; then
        skip "authz probe - scripts/authz-probe.sh not executable"
    else
        if ./scripts/authz-probe.sh; then ok "authz matrix"; else bad "authz matrix"; exit 1; fi
    fi
fi

# --- summary ------------------------------------------------------------------------------

step "Summary"
fails=0; skips=0
for entry in "${SUMMARY[@]}"; do
    status="${entry%%|*}"; label="${entry#*|}"
    case "$status" in
        PASS) echo -e "  ${GREEN}PASS${NC}  $label" ;;
        FAIL) echo -e "  ${RED}FAIL${NC}  $label"; fails=$((fails + 1)) ;;
        SKIP) echo -e "  ${YELLOW}SKIP${NC}  $label"; skips=$((skips + 1)) ;;
    esac
done

if [ "$fails" -gt 0 ]; then
    echo -e "\n${RED}$fails rung(s) failed.${NC}"; exit 1
fi
if [ "$skips" -gt 0 ]; then
    echo -e "\n${YELLOW}Completed with $skips skipped rung(s).${NC} Report these as skipped, not passed."
    exit 0
fi
echo -e "\n${GREEN}All rungs passed.${NC}"
