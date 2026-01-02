#!/bin/bash
# Bash script to get a JWT token from Keycloak for testing
# Usage: ./get-keycloak-token.sh [username] [password]

KEYCLOAK_URL="${KEYCLOAK_URL:-http://localhost:8180}"
REALM="${KEYCLOAK_REALM:-xr50}"
CLIENT_ID="${KEYCLOAK_CLIENT:-xr50-training-app}"
USERNAME="${1:-testuser}"
PASSWORD="${2:-testuser123}"

TOKEN_URL="$KEYCLOAK_URL/realms/$REALM/protocol/openid-connect/token"

echo -e "\033[36mGetting token from: $TOKEN_URL\033[0m"
echo -e "\033[36mUsername: $USERNAME\033[0m"

RESPONSE=$(curl -s -X POST "$TOKEN_URL" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=password" \
  -d "client_id=$CLIENT_ID" \
  -d "username=$USERNAME" \
  -d "password=$PASSWORD")

# Check if we got a token
ACCESS_TOKEN=$(echo "$RESPONSE" | jq -r '.access_token // empty')

if [ -z "$ACCESS_TOKEN" ]; then
  echo -e "\033[31mError getting token:\033[0m"
  echo "$RESPONSE" | jq .
  echo -e "\033[33mMake sure Keycloak is running at $KEYCLOAK_URL\033[0m"
  exit 1
fi

echo -e "\n\033[32m=== Access Token ===\033[0m"
echo "$ACCESS_TOKEN"

echo -e "\n\033[32m=== Token Info ===\033[0m"
echo "Token Type: $(echo "$RESPONSE" | jq -r '.token_type')"
echo "Expires In: $(echo "$RESPONSE" | jq -r '.expires_in') seconds"

echo -e "\n\033[32m=== Decoded Token Payload ===\033[0m"
echo "$ACCESS_TOKEN" | cut -d'.' -f2 | base64 -d 2>/dev/null | jq .

# Copy to clipboard if xclip is available
if command -v xclip &> /dev/null; then
  echo "$ACCESS_TOKEN" | xclip -selection clipboard
  echo -e "\n\033[33m[Token copied to clipboard]\033[0m"
elif command -v pbcopy &> /dev/null; then
  echo "$ACCESS_TOKEN" | pbcopy
  echo -e "\n\033[33m[Token copied to clipboard]\033[0m"
fi

echo -e "\n\033[36m=== For API Testing ===\033[0m"
echo "Authorization: Bearer ${ACCESS_TOKEN:0:50}..."

# Export for use in subsequent commands
export ACCESS_TOKEN
echo -e "\n\033[33mToken exported as \$ACCESS_TOKEN\033[0m"
