param([switch]$Supervise)
$ErrorActionPreference='Stop'
$workspace=(Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$stateRoot=Join-Path $workspace 'artifacts/worker-local'
[IO.Directory]::CreateDirectory($stateRoot) | Out-Null
$stopFile=Join-Path $stateRoot 'stop.request'
$stateFile=Join-Path $stateRoot 'state.json'
$dll=Join-Path $workspace 'src/BelemRestaurantes.Worker/bin/Release/net10.0/BelemRestaurantes.Worker.dll'
if(-not(Test-Path -LiteralPath $dll -PathType Leaf)){throw 'Compile o Worker em Release antes de iniciar.'}
$lockFile=Join-Path $stateRoot 'supervisor.lock'
if(-not $Supervise){
    try{$probe=[IO.File]::Open($lockFile,'OpenOrCreate','ReadWrite','None');$probe.Dispose()}
    catch{throw 'O supervisor local já está em execução.'}
    if(Test-Path -LiteralPath $stopFile){Remove-Item -LiteralPath $stopFile}
    $shell=(Get-Process -Id $PID).Path
    $process=Start-Process -FilePath $shell -ArgumentList @('-NoProfile','-File',('"'+$PSCommandPath+'"'),'-Supervise') -WindowStyle Hidden -PassThru
    Write-Output "Supervisor local iniciado: $($process.Id). Estado em artifacts/worker-local/state.json. Não instala serviço nem tarefa de inicialização."
    exit
}
$lease=[IO.File]::Open($lockFile,'OpenOrCreate','ReadWrite','None')
try{
    $env:DOTNET_ENVIRONMENT='Development';$env:ASPNETCORE_ENVIRONMENT='Development';$env:Worker__StopFile=$stopFile
    $failures=0
    while(-not(Test-Path -LiteralPath $stopFile)){
        $stamp=[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff')
        $child=Start-Process -FilePath 'dotnet' -ArgumentList @(('"'+$dll+'"')) -WorkingDirectory ([IO.Path]::GetDirectoryName($dll)) -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $stateRoot ($stamp+'.log')) -RedirectStandardError (Join-Path $stateRoot ($stamp+'.error.log'))
        @{supervisorPid=$PID;workerPid=$child.Id;startedAtUtc=[DateTime]::UtcNow.ToString('o');dll=$dll;state='running'} | ConvertTo-Json | Set-Content -LiteralPath $stateFile
        while(-not $child.WaitForExit(2000)){
            if(Test-Path -LiteralPath $stopFile){
                if(-not $child.WaitForExit(45000)){throw 'O Worker não encerrou no prazo. Confira filas e logs antes de encerrar manualmente.'}
                break
            }
        }
        if(Test-Path -LiteralPath $stopFile){break}
        $failures=[Math]::Min($failures+1,4)
        Start-Sleep -Seconds ([Math]::Min(60,5*[Math]::Pow(2,$failures-1)))
    }
    @{supervisorPid=$PID;workerPid=$null;state='stopped';atUtc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath $stateFile
}finally{$lease.Dispose()}
