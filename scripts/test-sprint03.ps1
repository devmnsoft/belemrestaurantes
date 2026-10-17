param()
$ErrorActionPreference = 'Stop'
$name = 'belem-sprint03-' + [Guid]::NewGuid().ToString('N').Substring(0, 10)
$variables = @('MYSQL_ROOT_PASSWORD', 'MYSQL_PASSWORD', 'MYSQL_DATABASE', 'MYSQL_USER', 'BELRESTAURANTES_TEST_MYSQL')
$saved = @{}
foreach ($variable in $variables) { $saved[$variable] = [Environment]::GetEnvironmentVariable($variable, 'Process') }
try {
    $env:MYSQL_ROOT_PASSWORD = [Guid]::NewGuid().ToString('N')
    $env:MYSQL_PASSWORD = [Guid]::NewGuid().ToString('N')
    $env:MYSQL_DATABASE = 'belrestaurante'
    $env:MYSQL_USER = 'sprint_test'
    docker run --detach --name $name --publish '127.0.0.1::3306' --env MYSQL_ROOT_PASSWORD --env MYSQL_PASSWORD --env MYSQL_DATABASE --env MYSQL_USER mysql:8.4 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Não foi possível criar o MySQL temporário.' }
    $ready = $false
    for ($attempt = 0; $attempt -lt 90; $attempt++) {
        docker exec $name mysqladmin --protocol=tcp --host=127.0.0.1 ping --silent 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) { $ready = $true; break }
        Start-Sleep -Seconds 2
    }
    if (-not $ready) { throw 'MySQL temporário não iniciou no prazo.' }
    $binding = docker port $name 3306/tcp
    $port = ($binding -split ':')[-1].Trim()
    $env:BELRESTAURANTES_TEST_MYSQL = "Server=127.0.0.1;Port=$port;Database=belrestaurante;User ID=sprint_test;Password=$env:MYSQL_PASSWORD;SslMode=Required"
    dotnet test BelemRestaurantes.sln -c Debug --nologo --logger 'trx;LogFilePrefix=sprint03' --results-directory artifacts/test-results
    if ($LASTEXITCODE -ne 0) { throw 'Há falhas nos testes; consulte artifacts/test-results.' }
    if (-not (Get-Command node -ErrorAction SilentlyContinue)) { throw 'Node.js é obrigatório para validar o JavaScript.' }
    node --test tests/frontend/account.test.cjs tests/frontend/site.test.cjs
    if ($LASTEXITCODE -ne 0) { throw 'Há falhas nos testes JavaScript.' }
}
finally {
    docker rm --force $name 2>$null | Out-Null
    foreach ($variable in $variables) { [Environment]::SetEnvironmentVariable($variable, $saved[$variable], 'Process') }
}
