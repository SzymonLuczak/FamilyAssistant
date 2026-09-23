param(
    [Parameter(Mandatory)]
    [ValidateSet('start', 'status', 'qr', 'groups', 'select-group', 'test-message')]
    [string]$Action,
    [string]$GroupId,
    [string]$Text = 'Family Assistant — ręczny test połączenia.',
    [string]$MessageId = ([guid]::NewGuid().ToString()),
    [switch]$ConfirmSend
)
$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '..')
try {
    $request = @{ path = '/status' }
    switch ($Action) {
        'start' { $request = @{ path = '/auth/start'; method = 'POST' } }
        'qr' { $request = @{ path = '/auth/qr' } }
        'groups' { $request = @{ path = '/groups' } }
        'select-group' {
            if (-not $GroupId) { throw 'Podaj -GroupId z listy groups.' }
            $request = @{ path = '/config/group'; method = 'PUT'; body = @{ groupId = $GroupId } }
        }
        'test-message' {
            $statusJson = '{"path":"/status"}' | docker compose exec -T whatsapp-gateway node dist/src/admin.js
            if ($LASTEXITCODE -ne 0) { throw 'Nie można odczytać statusu.' }
            $status = $statusJson | ConvertFrom-Json
            if ($status.sendEnabled -and -not $ConfirmSend) {
                throw 'Wysyłka jest aktywna. Ręczny test wymaga -ConfirmSend.'
            }
            $request = @{ path = '/messages'; method = 'POST'; body = @{ id = $MessageId; text = $Text } }
        }
    }
    $result = ($request | ConvertTo-Json -Compress) | docker compose exec -T whatsapp-gateway node dist/src/admin.js
    if ($LASTEXITCODE -ne 0) { throw 'Operacja WhatsApp nie powiodła się.' }
    if ($Action -eq 'qr') {
        New-Item -ItemType Directory -Force secrets | Out-Null
        $path = Join-Path $PWD 'secrets/whatsapp-qr.svg'
        [IO.File]::WriteAllText($path, ($result -join "`n"), [Text.UTF8Encoding]::new($false))
        Write-Host "Kod QR zapisany w $path. Otwórz plik i zeskanuj w WhatsApp > Połączone urządzenia."
        Write-Host 'Kod wygasa; ponów polecenie qr, aby pobrać aktualny.'
    } else { $result }
} finally { Pop-Location }
