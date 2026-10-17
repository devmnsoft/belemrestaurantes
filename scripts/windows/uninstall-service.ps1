param([string]$ServiceName = 'BelemRestauranteCuiaDiscovery')
$ErrorActionPreference = 'Stop'
$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $service) { Write-Host 'Serviço não instalado.'; return }
Stop-Service -Name $ServiceName
$service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(60))
sc.exe delete $ServiceName | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Falha ao remover registro do serviço.' }
Write-Host 'Serviço removido. Arquivos, configurações, logs e banco preservados.'
