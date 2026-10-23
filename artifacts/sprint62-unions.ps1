Add-Type -AssemblyName System.Net.Http | Out-Null
$c = [System.Net.Http.HttpClient]::new()
$c.Timeout = [TimeSpan]::FromSeconds(60)
$c.DefaultRequestHeaders.UserAgent.ParseAdd("CuiaCrawler/2.0 (+https://mnsisoft.com.br/cuia)")
$nl = "`n"
$variants = [ordered]@{
  "V1 uniao1elem"              = "[out:json][timeout:20];(nwr(around:50,-1.47,-48.5));out 1;"
  "V2 uniao2elem"              = "[out:json][timeout:20];(nwr(around:50,-1.47,-48.5);nwr(around:60,-1.47,-48.5));out 1;"
  "V3 multi-linha-tutorial"    = "[out:json][timeout:20];${nl}(${nl}  nwr[""highway""](around:50,-1.47,-48.5);${nl});${nl}out 1;"
  "V4 sem timeout no filtro"   = "[out:json];(nwr(around:50,-1.47,-48.5);nwr(around:60,-1.47,-48.5));out 1;"
}
foreach ($name in $variants.Keys) {
  $esc = [System.Uri]::EscapeDataString($variants[$name])
  try {
    $r = $c.GetAsync([System.Uri]"https://overpass.osm.ch/api/interpreter?data=$esc").GetAwaiter().GetResult()
    $body = $r.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    $err = if ($body -match '<strong[^>]*>Error</strong>: ([^<]+)') { $Matches[1].Trim() } else { "" }
    "$name -> $($r.StatusCode) $err"
  } catch {
    "$name -> ERRO: $($_.Exception.Message)"
  }
}
