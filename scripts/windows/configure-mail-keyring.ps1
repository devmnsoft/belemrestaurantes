param(
    [Parameter(Mandatory=$true)][string]$KeyRingPath,
    [string]$ApplicationPool='BelemRestaurantes',
    [string]$ServiceName='BelemRestauranteCuiaDiscovery'
)
$ErrorActionPreference='Stop'
if (-not [IO.Path]::IsPathFullyQualified($KeyRingPath)) { throw 'Informe um caminho absoluto privado fora do IIS.' }
$resolved=[IO.Path]::GetFullPath($KeyRingPath)
$programDataRoot=[IO.Path]::GetFullPath($env:ProgramData).TrimEnd('\')+'\'
if (-not $resolved.StartsWith($programDataRoot,[StringComparison]::OrdinalIgnoreCase)) {
    throw 'O diretório de chaves deve ser uma subpasta dedicada de ProgramData.'
}
if (Test-Path -LiteralPath $resolved) { throw 'Pasta já existe. Revise sua ACL manualmente para preservar permissões e chaves existentes.' }
New-Item -ItemType Directory -Path $resolved | Out-Null
icacls $resolved /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' "IIS AppPool\${ApplicationPool}:(OI)(CI)M" "NT SERVICE\${ServiceName}:(OI)(CI)M" | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Falha na ACL. Não use a pasta até revisar as permissões.' }
Write-Host 'Diretório privado preparado. Configure o mesmo MailEncryption__KeyRingPath na Web e no Worker.'
