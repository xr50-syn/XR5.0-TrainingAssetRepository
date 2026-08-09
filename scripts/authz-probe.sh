#!/bin/bash
# Stage 3 of docs/guides/authorization-test-plan.md - role matrix sweep.
#
# Drives one representative endpoint per authorization tier with each Keycloak realm identity
# and compares the status code against the expected matrix. Read-only: every probe is either a
# GET, or a mutation against an id/tenant that does not exist, so authorization is evaluated
# before the handler touches any state. Nothing is created and there is nothing to clean up.
#
# Usage: ./scripts/authz-probe.sh [tenantName]

set -uo pipefail

API_URL="${API_URL:-http://localhost:5286}"
KEYCLOAK_URL="${KEYCLOAK_URL:-http://localhost:8180}"
REALM="${KEYCLOAK_REALM:-xr50}"
CLIENT_ID="${KEYCLOAK_CLIENT:-xr50-training-app}"
TENANT="${1:-test_company}"
OTHER_TENANT="${OTHER_TENANT:-other_company_probe}"

GREEN='\033[0;32m'; RED='\033[0;31m'; YELLOW='\033[1;33m'; DIM='\033[2m'; NC='\033[0m'

pass_count=0; fail_count=0

# --- Stage 0 gate -------------------------------------------------------------------------
# The development anonymous bypass makes every policy succeed for anonymous callers, so the
# whole matrix would pass while proving nothing. Refuse to run rather than report a false green.
gate() {
    local code
    code=$(curl -s -m 10 -o /dev/null -w '%{http_code}' "$API_URL/api/auth/me")
    if [ "$code" = "000" ]; then
        echo -e "${RED}API unreachable at $API_URL${NC}"; exit 1
    fi
    if [ "$code" != "401" ]; then
        echo -e "${RED}GET /api/auth/me returned $code to an unauthenticated request; expected 401."
        echo -e "IAM__AllowAnonymousInDevelopment is almost certainly true, which disables every"
        echo -e "policy. This run would be meaningless. See plan step P0-2.${NC}"; exit 1
    fi
    echo -e "${GREEN}Stage 0 gate ok${NC} - anonymous /api/auth/me is 401, bypass is off."
}

token_for() {
    curl -s -m 10 -X POST "$KEYCLOAK_URL/realms/$REALM/protocol/openid-connect/token" \
        -H "Content-Type: application/x-www-form-urlencoded" \
        -d "grant_type=password" -d "client_id=$CLIENT_ID" \
        -d "username=$1" -d "password=$2" | jq -r '.access_token // empty'
}

status_for() {  # method path token
    # POST/PUT always carry a JSON content type and an empty body. Without them the model
    # binder answers 415 before the authorization verdict is observable, which would mask a
    # missing policy as an unrelated media-type error.
    local args=(-s -m 15 -o /dev/null -w '%{http_code}' -X "$1" "$API_URL$2")
    case "$1" in
        POST|PUT|PATCH) args+=(-H "Content-Type: application/json" -d '{}') ;;
    esac
    [ -n "$3" ] && args+=(-H "Authorization: Bearer $3")
    curl "${args[@]}"
}

# Expected forms: an exact code (403), or "authz-ok" meaning authorization passed - any status
# except 401/403. Handler-level 404/400/500 are fine; only the auth verdict is under test.
matches() {
    local expected="$1" actual="$2"
    case "$expected" in
        authz-ok) [ "$actual" != "401" ] && [ "$actual" != "403" ] ;;
        *)        [ "$expected" = "$actual" ] ;;
    esac
}

check() {  # label method path token expected
    local actual; actual=$(status_for "$2" "$3" "$4")
    if matches "$5" "$actual"; then
        pass_count=$((pass_count + 1))
        printf "  ${GREEN}ok${NC}   %-12s %-6s %-58s ${DIM}%s${NC}\n" "$1" "$2" "$3" "$actual"
    else
        fail_count=$((fail_count + 1))
        printf "  ${RED}FAIL${NC} %-12s %-6s %-58s ${RED}got %s, want %s${NC}\n" "$1" "$2" "$3" "$actual" "$5"
    fi
}

echo "=============================================================="
echo " Authorization role matrix - $API_URL (tenant: $TENANT)"
echo "=============================================================="
gate
echo ""

declare -A TOKENS
for pair in "testuser:testuser123" "tenantadmin:tenantadmin123" "admin:admin123" "sysadmin:sysadmin123"; do
    u="${pair%%:*}"; p="${pair##*:}"
    t=$(token_for "$u" "$p")
    if [ -z "$t" ]; then
        echo -e "${RED}Could not obtain a token for '$u' from $KEYCLOAK_URL (realm $REALM).${NC}"
        echo -e "${YELLOW}Is the sandbox profile up? keycloak only belongs to --profile sandbox.${NC}"
        exit 1
    fi
    TOKENS[$u]="$t"
    claim=$(echo "$t" | cut -d. -f2 | base64 -d 2>/dev/null | jq -c '{preferred_username,tenantName,role}')
    echo -e "  token ${GREEN}$u${NC} ${DIM}$claim${NC}"
done
echo ""

# Stale-build check. The fallback policy 401s unmatched routes too, so an anonymous request
# cannot distinguish "endpoint missing" from "auth required" - it takes a real credential.
if [ "$(status_for GET /api/auth/me "${TOKENS[testuser]}")" = "404" ]; then
    echo -e "${RED}GET /api/auth/me is 404 for an authenticated caller - the running image"
    echo -e "predates the authorization change. Rebuild before probing (plan step P0-1).${NC}"
    exit 1
fi

# label            method  path                                                        anon      testuser  tenantadmin  sysadmin
ROWS=(
"anonymous       |GET   |/health                                                      |200      |200      |200      |200"
"anonymous       |GET   |/api/test                                                    |200      |200      |200      |200"
"fallback        |GET   |/api/auth/me                                                 |401      |200      |200      |200"
"fallback        |GET   |/xr50/trainingAssetRepository/Tenants/examples/create-requests|401     |authz-ok |authz-ok |authz-ok"
"TenantMember    |GET   |/api/TENANT/materials                                        |401      |authz-ok |authz-ok |authz-ok"
"TenantMember    |GET   |/api/TENANT/users                                            |401      |authz-ok |authz-ok |authz-ok"
"TenantAdmin     |DELETE|/api/TENANT/materials/999999                                 |401      |403      |authz-ok |authz-ok"
"TenantAdmin     |PUT   |/api/TENANT/materials/999999                                 |401      |403      |authz-ok |authz-ok"
"TenantAdmin     |POST  |/api/TENANT/materials/999999/assign-asset/999999             |401      |403      |authz-ok |authz-ok"
"TenantAdmin     |DELETE|/api/TENANT/innov-chatbot/1/history                          |401      |403      |authz-ok |authz-ok"
"SystemAdmin     |GET   |/xr50/trainingAssetRepository/Tenants                         |401      |403      |403      |authz-ok"
"TenantCreator   |POST  |/xr50/trainingAssetRepository/Tenants                         |401      |403      |403      |authz-ok"
"SystemAdmin     |DELETE|/xr50/trainingAssetRepository/Tenants/nonexistent_probe       |401      |403      |403      |authz-ok"
"SystemAdmin     |GET   |/api/troubleshooting/health-check                            |401      |403      |403      |authz-ok"
"cross-tenant    |GET   |/api/OTHER/materials                                         |401      |403      |403      |authz-ok"
)

for who in anonymous testuser tenantadmin sysadmin; do
    case "$who" in
        anonymous) tok=""; col=4 ;;
        testuser)  tok="${TOKENS[testuser]}"; col=5 ;;
        tenantadmin) tok="${TOKENS[tenantadmin]}"; col=6 ;;
        sysadmin)  tok="${TOKENS[sysadmin]}"; col=7 ;;
    esac
    echo -e "${YELLOW}--- as $who ---${NC}"
    for row in "${ROWS[@]}"; do
        IFS='|' read -r label method path e_anon e_user e_tadmin e_sadmin <<< "$row"
        label=$(echo "$label" | xargs); method=$(echo "$method" | xargs); path=$(echo "$path" | xargs)
        path="${path//TENANT/$TENANT}"; path="${path//OTHER/$OTHER_TENANT}"
        case $col in
            4) exp=$(echo "$e_anon" | xargs) ;;
            5) exp=$(echo "$e_user" | xargs) ;;
            6) exp=$(echo "$e_tadmin" | xargs) ;;
            7) exp=$(echo "$e_sadmin" | xargs) ;;
        esac
        check "$label" "$method" "$path" "$tok" "$exp"
    done
    echo ""
done

# 'admin' is a TenantAdminRoles alias; spot-check that it behaves like tenantadmin.
echo -e "${YELLOW}--- role alias: 'admin' must behave as tenant admin ---${NC}"
check "alias" DELETE "/api/$TENANT/materials/999999" "${TOKENS[admin]}" authz-ok
check "alias" GET    "/xr50/trainingAssetRepository/Tenants" "${TOKENS[admin]}" 403
echo ""

# The bulk-authoring endpoints are checked for the member only. A 403 is decided by the
# authorization middleware before the handler runs, so nothing is created. They are deliberately
# NOT probed as an admin: with a passing policy they would reach the handler, which accepts an
# empty body and creates a material (observed: 201 from '{}').
echo -e "${YELLOW}--- bulk authoring endpoints are TenantAdmin (member must be refused) ---${NC}"
for route in workflow-complete video-complete checklist-complete; do
    check "authoring" POST "/api/$TENANT/materials/$route" "${TOKENS[testuser]}" 403
done
echo ""

echo -e "${YELLOW}--- malformed bearer token must not authenticate ---${NC}"
check "bad-token" GET "/api/$TENANT/materials" "not-a-real-jwt" 401
echo ""

echo "=============================================================="
if [ "$fail_count" -eq 0 ]; then
    echo -e "${GREEN}All $pass_count probes matched the expected matrix.${NC}"
    exit 0
fi
echo -e "${RED}$fail_count of $((pass_count + fail_count)) probes did not match.${NC}"
echo -e "${DIM}A 200/2xx where 403 was expected is over-permissioning."
echo -e "A 403 where authz-ok was expected is a broken legitimate operation.${NC}"
exit 1
