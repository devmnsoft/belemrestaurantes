param(
    [ValidateSet('legacy','expanded')][string]$Profile='expanded',
    [string]$Output='artifacts/food-expansion',
    [ValidateRange(1,100000)][int]$Target=100000,
    [ValidateRange(1,2000)][int]$MaxPages=2000,
    [ValidateRange(1,50)][int]$BatchPages=10
)
$ErrorActionPreference='Stop'
$taskRepository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Set-Location -LiteralPath $taskRepository
$taskOutput=[IO.Path]::GetFullPath((Join-Path $taskRepository $Output))
if(-not $taskOutput.StartsWith($taskRepository+'\',[StringComparison]::OrdinalIgnoreCase)) {
    throw 'O diretório de coleta deve permanecer dentro do projeto.'
}
New-Item -ItemType Directory -Force -Path $taskOutput | Out-Null
# Uma única orquestração por diretório. O bloqueio é liberado inclusive ao encerrar o processo.
$taskLock=[IO.File]::Open((Join-Path $taskOutput 'pipeline.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
try {
    $taskData=Join-Path $taskOutput 'food-pages.ndjson'
    $taskCheckpoint=Join-Path $taskOutput 'checkpoint.json'
    $taskManifest=Join-Path $taskOutput 'manifest.json'
    $taskSnapshot=Join-Path $taskOutput 'import-snapshot.ndjson'
    $taskImportLog=Join-Path $taskOutput 'import.log'
    function Import-ConfirmedSnapshot {
        if(-not (Test-Path -LiteralPath $taskData)) { return }
        Copy-Item -LiteralPath $taskData -Destination $taskSnapshot -Force
        & dotnet run --project src/BelemRestaurantes.Worker -c Release --no-build --no-launch-profile -- --environment Development --import-food-registry "--file=$taskSnapshot" "--max-new=$Target" *> $taskImportLog
        if($LASTEXITCODE -ne 0) { throw "Importação pausada. Consulte $taskImportLog; o replay preserva os CNPJs já inseridos." }
        Get-Content -LiteralPath $taskImportLog -Tail 1
    }
    Import-ConfirmedSnapshot
    while($true) {
        $taskBefore=if(Test-Path -LiteralPath $taskManifest){@(Get-Content -Raw -LiteralPath $taskManifest | ConvertFrom-Json).Count}else{0}
        if(Test-Path -LiteralPath $taskCheckpoint) {
            $taskState=Get-Content -Raw -LiteralPath $taskCheckpoint | ConvertFrom-Json
            if($taskState.complete) { Write-Output 'Fonte esgotada para este perfil. Importação concluída; cadastros aguardam moderação.'; break }
        }
        if($taskBefore -ge $MaxPages) { Write-Output 'Limite de páginas desta execução atingido. Cursor salvo para retomada.'; break }
        if($taskBefore -gt 0) {
            $taskEntries=@(Get-Content -Raw -LiteralPath $taskManifest | ConvertFrom-Json)
            if($taskEntries[-1].activeNamedAccumulated -ge $Target) { Write-Output 'Meta de candidatos coletados atingida. Confira os totais reais no painel de cobertura.'; break }
        }
        $taskNext=[Math]::Min($MaxPages,$taskBefore+$BatchPages)
        & python scripts/fetch-food-registry.py --profile $Profile --output $taskOutput --target $Target --max-pages $taskNext
        $taskCollectorExit=$LASTEXITCODE
        $taskAfter=if(Test-Path -LiteralPath $taskManifest){@(Get-Content -Raw -LiteralPath $taskManifest | ConvertFrom-Json).Count}else{0}
        if($taskAfter -gt $taskBefore) { Import-ConfirmedSnapshot }
        if($taskCollectorExit -ne 0) { throw 'Coleta pausada; páginas válidas importadas. Confira o erro acima e retome com o mesmo diretório.' }
        if($taskAfter -le $taskBefore) { throw 'A fonte não avançou. Processo pausado, sem contornar o limite da API.' }
    }
} finally { $taskLock.Dispose() }
