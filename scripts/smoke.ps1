param([int]$CorePort = 8080)
$ErrorActionPreference = 'Stop'
foreach ($port in @($CorePort)) {
    $response = Invoke-WebRequest "http://127.0.0.1:$port/health" -TimeoutSec 10
    if ($response.StatusCode -ne 200) { throw "Unhealthy service on port $port" }
    $status = Invoke-RestMethod "http://127.0.0.1:$port/" -TimeoutSec 10
    if ($status.integrations -ne 'read_only') { throw "Unexpected integration mode on port $port" }
    Write-Host "$($status.service): healthy, integrations read only"
}
Push-Location (Join-Path $PSScriptRoot '..')
try {
    foreach ($url in @('http://vulcan-gateway:8000/health', 'http://whatsapp-gateway:3000/health')) {
        docker compose exec -T family-core curl --fail --silent --max-time 5 $url
        if ($LASTEXITCODE -ne 0) { throw "Gateway unhealthy: $url" }
    }
    $integrations = Invoke-RestMethod "http://127.0.0.1:$CorePort/health/integrations" -TimeoutSec 10
    Write-Host "VULCAN: $($integrations.vulcan)"
    Write-Host "WhatsApp connection: $($integrations.whatsapp)"
    Write-Host "Google Calendar: $($integrations.googleCalendar)"
} finally { Pop-Location }
