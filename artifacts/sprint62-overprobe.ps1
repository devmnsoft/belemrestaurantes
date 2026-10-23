Add-Type -AssemblyName System.Net.Http | Out-Null
$min = '[out:json][timeout:10];nwr(around:50,-1.47,-48.5);out 1;'
$escaped = [System.Uri]::EscapeDataString($min)
$c = [System.Net.Http.HttpClient]::new()
$c.Timeout = [TimeSpan]::FromSeconds(90)
$c.DefaultRequestHeaders.UserAgent.ParseAdd("CuiaCrawler/2.0 (+https://mnsisoft.com.br/cuia)")
$sb = New-Object System.Text.StringBuilder
foreach ($h in @("overpass.kumi.systems")) {
    $s = "https://{0}/api/interpreter?data={1}" -f $h, $escaped
    $uri = [System.Uri]$s
    try {
        $r = $c.GetAsync($uri).GetAwaiter().GetResult()
        $body = $r.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        $snippet = if ($body.Length -gt 0) { $body.Substring(0,[Math]::Min(140,$body.Length)).Replace("`n"," ") } else { "(corpo vazio)" }
        [void]$sb.AppendLine("$h GET $($r.StatusCode) len=$($body.Length) :: $snippet")
    } catch {
        [void]$sb.AppendLine("$h GET ERRO: $($_.Exception.Message)")
    }
}
[System.IO.File]::WriteAllText("$PSScriptRoot\sprint62-overpass-probe.txt", $sb.ToString(), [System.Text.Encoding]::UTF8)
Write-Host $sb.ToString()
