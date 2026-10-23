Add-Type -AssemblyName System.Net.Http | Out-Null
$c = [System.Net.Http.HttpClient]::new()
$c.Timeout = [TimeSpan]::FromSeconds(60)
$c.DefaultRequestHeaders.UserAgent.ParseAdd("CuiaCrawler/2.0 (+https://mnsisoft.com.br/cuia)")
$q1 = "[out:json][timeout:20];way(331449259);out center tags;"
$s1 = "https://overpass.osm.ch/api/interpreter?data=" + [System.Uri]::EscapeDataString($q1)
$r1 = $c.GetAsync([System.Uri]$s1).GetAwaiter().GetResult()
$b1 = $r1.Content.ReadAsStringAsync().GetAwaiter().GetResult()
"WAY na osm.ch: $($r1.StatusCode) len=$($b1.Length)"
"$(($b1 -replace '\s+',' ').Substring(0,[Math]::Min(700,$b1.Length)))"
