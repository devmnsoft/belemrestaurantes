param(
    [Parameter(Mandatory = $true)][string]$WorkerDirectory
)
$ErrorActionPreference = 'Stop'
$serviceName = 'BelemRestauranteCuiaDiscovery'
$workerRoot = (Resolve-Path -LiteralPath $WorkerDirectory).Path
$iisRoot = [IO.Path]::GetFullPath((Join-Path $env:SystemDrive 'inetpub'))
if ($workerRoot.StartsWith($iisRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'O Worker deve ficar fora da pasta IIS.'
}
$exePath = Join-Path $workerRoot 'BelemRestaurantes.Worker.exe'
if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) { throw 'Executável do Worker não encontrado.' }
if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
    throw 'Serviço já instalado. Pare o serviço e atualize os arquivos; não recriar automaticamente.'
}
New-Service -Name $serviceName -BinaryPathName ('"' + $exePath + '"') -DisplayName 'Belém Restaurantes — Cuia Discovery' -StartupType Automatic
sc.exe config $serviceName obj= "NT SERVICE\$serviceName" | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Falha ao configurar identidade dedicada. Não iniciar o serviço.' }
sc.exe failure $serviceName reset= 86400 actions= restart/60000/restart/120000/restart/300000 | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Falha ao configurar recuperação.' }
Write-Host "Serviço instalado com identidade NT SERVICE\$serviceName."
Write-Host 'Conceda leitura/execução na pasta do Worker, configure segredos privados e Database:ApplyMigrations=false antes de iniciar.'
Write-Host "Após validar as permissões, execute Start-Service $serviceName."
