param(
 [string]$Project = "src/BelemRestaurantes.Web/BelemRestaurantes.Web.csproj",
 [string]$Output = "artifacts/publish"
)
$ErrorActionPreference = "Stop"
dotnet --info
dotnet restore $Project
if ($LASTEXITCODE -ne 0) { throw "Restore falhou" }
dotnet publish $Project -c Release -o $Output --no-restore
if ($LASTEXITCODE -ne 0) { throw "Publish falhou" }
Write-Output "Artefatos em: $Output"
