# install-service.ps1 — registra o Cuia Discovery como Windows Service.
# Executar em PowerShell elevado (Administrador).

$serviceName = "BelemRestauranteCuiaDiscovery"
$displayName  = "Belém Restaurantes — Cuia Discovery"
$description  = "Descoberta gastronômica automática (cuia) a cada 10 minutos."
$exePath      = (Resolve-Path ".\publish\BelemRestaurantes.Worker.exe").Path

if (-not (Test-Path $exePath)) {
    Write-Error "Publicação não encontrada em $exePath. Rode publish.ps1 antes."
    exit 1
}

$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $serviceName | Out-Null
    Start-Sleep -Seconds 2
}

New-Service -Name $serviceName -BinaryPathName "`"$exePath`"" -DisplayName $displayName -Description $description -StartupType Automatic
sc.exe failure $serviceName reset= 86400 actions= restart/60000/restart/60000/restart/120000 | Out-Null
Start-Service -Name $serviceName
Write-Host "Service $serviceName instalado e iniciado."