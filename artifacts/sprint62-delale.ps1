Add-Type -AssemblyName System.Net.Http | Out-Null
$sb = New-Object System.Text.StringBuilder
$c = [System.Net.Http.HttpClient]::new()
$c.Timeout = [TimeSpan]::FromSeconds(90)
$c.DefaultRequestHeaders.UserAgent.ParseAdd("CuiaCrawler/2.0 (+https://mnsisoft.com.br/cuia)")
$nl = "`n"
$r = 80; $lat = "-1.458131"; $lon = "-48.494855"
$q = "[out:json][timeout:20];${nl}(${nl}  nwr[""building""](around:${r},${lat},${lon});${nl}  nwr[""amenity""~""^(restaurant|fast_food|cafe|bar|pub|food_court)$""](around:${r},${lat},${lon});${nl}  nwr[""shop""=""restaurant""](around:${r},${lat},${lon});${nl}  nwr[""addr:housenumber""](around:${r},${lat},${lon});${nl});${nl}out center tags;"
$escaped = [System.Uri]::EscapeDataString($q)
$hosts = @("overpass-api.de","overpass.kumi.systems","overpass.osm.ch")
$firstJson = $null
foreach ($h in $hosts) {
    $uriStr = "https://$h/api/interpreter?data=$escaped"
    try {
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $resp = $c.GetAsync([System.Uri]$uriStr).GetAwaiter().GetResult()
        $body = $resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        if ($resp.StatusCode -eq 200) {
            $j = $body | ConvertFrom-Json
            [void]$sb.AppendLine("$h -> OK em $([int]$sw.ElapsedMilliseconds)ms elementos=$($j.elements.Count)")
            foreach ($e in $j.elements) {
                $lat2 = if ($e.PSObject.Properties['lat']) { $e.lat } else { $e.center.lat }
                $lon2 = if ($e.PSObject.Properties['lon']) { $e.lon } else { $e.center.lon }
                $t = $e.tags
                [void]$sb.AppendLine(("   {0} id={1} ({2},{3})" -f $e.type, $e.id, $lat2, $lon2))
                foreach ($k in @('name','addr:street','addr:housenumber','building','amenity','shop')) {
                    if ($t.PSObject.Properties[$k]) { [void]$sb.AppendLine(("      {0} = {1}" -f $k, $t.$k)) }
                }
            }
            if ($null -eq $firstJson) { $firstJson = $body }
        } else {
            $err = ""
            if ($body -match '<strong[^>]*>Error</strong>: ([^<]+)') { $err = $Matches[1].Trim() }
            [void]$sb.AppendLine("$h -> $($resp.StatusCode) em $([int]$sw.ElapsedMilliseconds)ms $err")
        }
    } catch {
        [void]$sb.AppendLine("$h -> ERRO: $($_.Exception.Message)")
    }
}
if ($null -ne $firstJson) { [System.IO.File]::WriteAllText("$PSScriptRoot\sprint62-delale-overpass.json", $firstJson, [System.Text.Encoding]::UTF8) }
[System.IO.File]::WriteAllText("$PSScriptRoot\sprint62-delale-all-endpoints.txt", $sb.ToString(), [System.Text.Encoding]::UTF8)
Write-Host $sb.ToString()
