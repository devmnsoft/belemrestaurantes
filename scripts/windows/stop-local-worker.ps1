$ErrorActionPreference='Stop'
$workspace=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$stateRoot=Join-Path $workspace 'artifacts/worker-local'
if(-not(Test-Path -LiteralPath $stateRoot)){Write-Output 'Supervisor local não iniciado.';exit}
[IO.File]::WriteAllText((Join-Path $stateRoot 'stop.request'),[DateTime]::UtcNow.ToString('o'))
Write-Output 'Parada ordenada solicitada. O supervisor deixa de reiniciar e o Worker conclui o cancelamento das operações. Confira state.json e o heartbeat.'
