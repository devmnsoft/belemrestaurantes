Add-Type -AssemblyName System.Net.Http | Out-Null
$sb = New-Object System.Text.StringBuilder
$c = [System.Net.Http.HttpClient]::new()
$c.Timeout = [TimeSpan]::FromSeconds(60)
$c.DefaultRequestHeaders.UserAgent.ParseAdd("CuiaCrawler/2.0 (+https://mnsisoft.com.br/cuia)")
$around = 'around:80,-1.458131,-48.494855'
$tests = [ordered]@{
    "A build+amenity"  = "[out:json][timeout:20];(nwr[""building""]($around);nwr[""amenity""~""^(restaurant|fast_food|cafe|bar|pub|food_court)$""]($around));out center tags;"
    "B build+shop"     = "[out:json][timeout:20];(nwr[""building""]($around);nwr[""shop""=""restaurant""]($around));out center tags;"
    "C build+addr"     = "[out:json][timeout:20];(nwr[""building""]($around);nwr[""addr:housenumber""]($around));out center tags;"
    "D amenity alone"  = "[out:json][timeout:20];nwr[""amenity""~""^(restaurant|fast_food|cafe|bar|pub|food_court)$""]($around);out center tags;"
}
foreach ($name in $tests.Keys) {
    $escaped = [System.Uri]::EscapeDataString($tests[$name])
    try {
        $r = $c.GetAsync([System.Uri]"https://overpass.osm.ch/api/interpreter?data=$escaped").GetAwaiter().GetResult()
        $body = $r.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        $count = if ($body -match '"elements"\s*:\s*\[') { ($body | ConvertFrom-Json).elements.Count } else { "?" }
        $err = if ($body -match '<strong[^>]*>Error</strong>: ([^<]+)') { $Matches[1].Trim() } else { "" }
        [void]$sb.AppendLine("$name -> $($r.StatusCode) elementos=$count $err")
    } catch {
        [void]$sb.AppendLine("$name -> ERRO: $($_.Exception.Message)")
    }
}
[System.IO.File]::WriteAllText("$PSScriptRoot\sprint62-selectors-probe.txt", $sb.ToString(), [System.Text.Encoding]::UTF8)
Write-Host $sb.ToString()
