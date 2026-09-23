$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '..')
try {
    docker compose -f compose.test.yaml build
    if ($LASTEXITCODE -ne 0) { throw 'Test image build failed' }
    foreach ($service in @('core-tests', 'vulcan-tests', 'whatsapp-tests')) {
        docker compose -f compose.test.yaml run --rm $service
        if ($LASTEXITCODE -ne 0) { throw "Tests failed: $service" }
    }
} finally { Pop-Location }
