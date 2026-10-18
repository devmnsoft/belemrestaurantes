# Execute pessoalmente em um terminal local privado. Não redirecione para logs.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security
$taskCredentialPath = Join-Path $env:LOCALAPPDATA 'MNSOFT\BelemRestaurantes\homologacao.credentials.dpapi'
$taskEncrypted = [IO.File]::ReadAllBytes($taskCredentialPath)
$taskPlain = [Security.Cryptography.ProtectedData]::Unprotect($taskEncrypted, $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)
try {
    [Text.Encoding]::UTF8.GetString($taskPlain) | ConvertFrom-Json | Format-List
} finally { [Array]::Clear($taskPlain, 0, $taskPlain.Length) }
