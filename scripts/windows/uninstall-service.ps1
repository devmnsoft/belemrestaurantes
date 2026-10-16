# uninstall-service.ps1
$serviceName = "BelemRestauranteCuiaDiscovery"
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($service) {
    Stop-Service -Name $serviceName -Force
    sc.exe delete $serviceName | Out-Null
    Write-Host "Service removido."
} else {
    Write-Host "Service não encontrado."
}