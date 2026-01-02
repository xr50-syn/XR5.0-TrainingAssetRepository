# PowerShell script to get a JWT token from Keycloak for testing
# Usage: .\get-keycloak-token.ps1 [-Username testuser] [-Password testuser123]

param(
    [string]$KeycloakUrl = "http://localhost:8180",
    [string]$Realm = "xr50",
    [string]$ClientId = "xr50-training-app",
    [string]$Username = "testuser",
    [string]$Password = "testuser123"
)

$tokenUrl = "$KeycloakUrl/realms/$Realm/protocol/openid-connect/token"

Write-Host "Getting token from: $tokenUrl" -ForegroundColor Cyan
Write-Host "Username: $Username" -ForegroundColor Cyan

try {
    $body = @{
        grant_type = "password"
        client_id  = $ClientId
        username   = $Username
        password   = $Password
    }

    $response = Invoke-RestMethod -Uri $tokenUrl -Method Post -Body $body -ContentType "application/x-www-form-urlencoded"

    Write-Host "`n=== Access Token ===" -ForegroundColor Green
    Write-Host $response.access_token

    Write-Host "`n=== Token Info ===" -ForegroundColor Green
    Write-Host "Token Type: $($response.token_type)"
    Write-Host "Expires In: $($response.expires_in) seconds"
    Write-Host "Refresh Expires In: $($response.refresh_expires_in) seconds"

    # Decode JWT payload (base64)
    $tokenParts = $response.access_token.Split('.')
    if ($tokenParts.Length -eq 3) {
        $payload = $tokenParts[1]
        # Add padding if needed
        $padding = 4 - ($payload.Length % 4)
        if ($padding -ne 4) {
            $payload += "=" * $padding
        }
        $decodedPayload = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($payload))
        Write-Host "`n=== Decoded Token Payload ===" -ForegroundColor Green
        $decodedPayload | ConvertFrom-Json | ConvertTo-Json -Depth 10
    }

    # Copy to clipboard if available
    if (Get-Command Set-Clipboard -ErrorAction SilentlyContinue) {
        $response.access_token | Set-Clipboard
        Write-Host "`n[Token copied to clipboard]" -ForegroundColor Yellow
    }

    # Output for use in scripts
    Write-Host "`n=== For API Testing ===" -ForegroundColor Cyan
    Write-Host "Authorization: Bearer $($response.access_token.Substring(0, 50))..."

    return $response
}
catch {
    Write-Host "Error getting token: $_" -ForegroundColor Red
    Write-Host "Make sure Keycloak is running at $KeycloakUrl" -ForegroundColor Yellow
    exit 1
}
