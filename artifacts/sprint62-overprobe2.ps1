Add-Type -AssemblyName System.Net.Http | Out-Null
$hosts = @("overpass.kumi.systems","overpass.osm.jp","overpass.osm.ch")
$sb = New-Object System.Text.StringBuilder
foreach ($h in $hosts) {
    try {
        $wc = [System.Net.WebClient]::new()
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $html = $wc.DownloadString("https://$h/")
        $sw.Stop()
        [void]$sb.AppendLine("$h root: $($html.Length) chars em $([int]$sw.ElapsedMilliseconds)ms")
        $wc.Dispose()
    } catch {
        [void]$sb.AppendLine("$h root ERRO: $($_.Exception.Message)")
    }
}
[System.IO.File]::WriteAllText("$PSScriptRoot\sprint62-overpass-probe2.txt", $sb.ToString(), [System.Text.Encoding]::UTF8)
Write-Host $sb.ToString()
