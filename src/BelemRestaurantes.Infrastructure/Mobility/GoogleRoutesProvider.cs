using System.Net.Http.Json;
using System.Text.Json;
using BelemRestaurantes.Application.Models;
using BelemRestaurantes.Application.Services;
using BelemRestaurantes.Infrastructure.MySql;
using Dapper;
using Microsoft.Extensions.Configuration;

namespace BelemRestaurantes.Infrastructure.Mobility;

/// <summary>No persistent cache of Google route content or visitor origin. Only aggregate request counters.</summary>
public sealed class GoogleRoutesProvider(IHttpClientFactory clients,IConfiguration config,MySqlConnectionFactory factory):IMobilityProvider
{
    public string Name=>"Google Routes";
    public bool IsConfigured=>config.GetValue<bool>("GoogleRoutes:Enabled")&&config.GetValue<int>("GoogleRoutes:DailyRequestLimit")>0
        &&config["GoogleRoutes:ApiKey"] is {} key&&key.StartsWith("AIza",StringComparison.Ordinal);
    public async Task<RouteResult> PlanAsync(VisitRequest request,RestaurantView restaurant,CancellationToken ct)
    {
        var link=VisitLinks.Maps(restaurant,request.Origin,request.Mode);
        RouteResult Fallback(string state,string message)=>new(Name,state,message,DateTimeOffset.UtcNow,[],link);
        if(request.Mode=="boat")return Fallback("unavailable","Travessia fluvial precisa de confirmação com o operador. Consulte as fontes oficiais abaixo; o Maps pode não cobrir o trecho de barco.");
        if(!IsConfigured)return Fallback("unconfigured","Consulte linhas, trajetos e tempos no Google Maps. A consulta interna ainda não está disponível.");
        if(request.Accessible)return Fallback("unavailable","Este provedor não confirma acessibilidade integral do trajeto. Consulte o operador antes de viajar.");
        if(string.IsNullOrWhiteSpace(request.Origin))return Fallback("unavailable","Informe um endereço, bairro ou ponto de referência de origem.");
        if(!await ReserveAsync(ct))return Fallback("budget","O limite diário de consultas internas foi atingido. Continue no Google Maps.");
        var mode=request.Mode switch{"walking"=>"WALK","bicycling"=>"BICYCLE","driving"=>"DRIVE",_=>"TRANSIT"};
        var body=new Dictionary<string,object>{["origin"]=Waypoint(request.Origin),["destination"]=restaurant.Latitude is {} lat&&restaurant.Longitude is {} lng&&CatalogMapLayout.IsPlottable(lat,lng)?new{location=new{latLng=new{latitude=lat,longitude=lng}}}:Waypoint(VisitLinks.Destination(restaurant)),["travelMode"]=mode,["computeAlternativeRoutes"]=true,["languageCode"]="pt-BR",["regionCode"]="BR",["units"]="METRIC"};
        if(request.LocalTime is {} wall)
        {
            var instant=new DateTimeOffset(DateTime.SpecifyKind(wall,DateTimeKind.Unspecified),OpeningHours.ParaZone.GetUtcOffset(wall));
            if(mode=="TRANSIT")body[request.Arrival?"arrivalTime":"departureTime"]=instant.ToUniversalTime().ToString("O");
            else if(mode=="DRIVE"&&!request.Arrival)body["departureTime"]=instant.ToUniversalTime().ToString("O");
        }
        if(mode=="TRANSIT")body["transitPreferences"]=new{routingPreference=request.Preference=="less_transfers"?"FEWER_TRANSFERS":"LESS_WALKING"};
        using var message=new HttpRequestMessage(HttpMethod.Post,"https://routes.googleapis.com/directions/v2:computeRoutes"){Content=JsonContent.Create(body)};
        message.Headers.Add("X-Goog-Api-Key",config["GoogleRoutes:ApiKey"]);
        message.Headers.Add("X-Goog-FieldMask","routes.description,routes.distanceMeters,routes.duration,routes.polyline.encodedPolyline,routes.legs.steps.travelMode,routes.legs.steps.distanceMeters,routes.legs.steps.navigationInstruction.instructions,routes.legs.steps.staticDuration,routes.legs.steps.polyline.encodedPolyline,routes.legs.steps.transitDetails,routes.travelAdvisory.transitFare");
        var watch=System.Diagnostics.Stopwatch.StartNew();
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            using var response=await clients.CreateClient("routes").SendAsync(message,HttpCompletionOption.ResponseHeadersRead,timeout.Token);
            if(!response.IsSuccessStatusCode)
            {await FailAsync("HTTP "+(int)response.StatusCode,ct);return (int)response.StatusCode switch {401 or 403=>Fallback("configuration_error","A consulta de trajetos está temporariamente indisponível neste portal. Continue no Google Maps."),429=>Fallback("budget","O limite de consultas do provedor foi atingido. Continue no Google Maps."),_=>Fallback("error","O provedor não retornou o trajeto. Tente no Google Maps.")};}
            await using var stream=await response.Content.ReadAsStreamAsync(timeout.Token);
            using var document=await JsonDocument.ParseAsync(stream,cancellationToken:timeout.Token);
            var alternatives=Parse(document.RootElement,request.MaxWalkMeters);
            return new(Name,alternatives.Count==0?"unavailable":"available",alternatives.Count==0?"Nenhuma rota compatível com o limite de caminhada foi informada. Consulte alternativas no Maps.":"Estimativas fornecidas pelo Google Maps; confirme alterações, tarifas e acessibilidade com o operador.",DateTimeOffset.UtcNow,alternatives,link);
        }
        catch(OperationCanceledException) when(!ct.IsCancellationRequested){await FailAsync("timeout",ct);return Fallback("error","A consulta demorou além do limite. Continue no Google Maps.");}
        catch(HttpRequestException){await FailAsync("network",ct);return Fallback("error","Serviço de trajetos temporariamente indisponível.");}
        catch(JsonException){await FailAsync("invalid_response",ct);return Fallback("error","O provedor retornou informações incompletas.");}
        finally { MapDiagnostics.RoutesMilliseconds.Record(watch.Elapsed.TotalMilliseconds); }
    }
    public static IReadOnlyList<RouteAlternative> Parse(JsonElement root,int maxWalkMeters)
    {
        string? Text(JsonElement element,string name)=>element.ValueKind==JsonValueKind.Object&&element.TryGetProperty(name,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString():null;
        JsonElement Child(JsonElement element,string name)=>element.ValueKind==JsonValueKind.Object&&element.TryGetProperty(name,out var v)?v:default;
        int Number(JsonElement element,string name)=>Child(element,name).ValueKind==JsonValueKind.Number&&Child(element,name).TryGetInt32(out var n)?n:0;
        var results=new List<RouteAlternative>();
        if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("routes",out var routes)||routes.ValueKind!=JsonValueKind.Array)return results;
        foreach(var route in routes.EnumerateArray().Take(3))
        {
            var steps=new List<RouteStep>();var walk=0L;
            var legs=Child(route,"legs");
            if(legs.ValueKind==JsonValueKind.Array)foreach(var leg in legs.EnumerateArray().Take(8))
            {var segments=Child(leg,"steps");if(segments.ValueKind!=JsonValueKind.Array)continue;foreach(var step in segments.EnumerateArray().Take(256))
            {
                var mode=Text(step,"travelMode")??"";var distance=Number(step,"distanceMeters");if(mode=="WALK")walk+=distance;
                var transit=Child(step,"transitDetails");var stops=Child(transit,"stopDetails");var line=Child(transit,"transitLine");
                steps.Add(new(mode,Text(Child(step,"navigationInstruction"),"instructions")??Text(transit,"headsign")??"Siga o trecho informado pelo provedor",
                    Text(line,"nameShort")??Text(line,"name"),Text(Child(stops,"departureStop"),"name"),Text(Child(stops,"arrivalStop"),"name"),Text(stops,"departureTime"),Text(stops,"arrivalTime"),distance) {
                    Headsign=Text(transit,"headsign"),DurationSeconds=Seconds(Text(step,"staticDuration")),
                    Path=RouteGeometry.Decode(Text(Child(step,"polyline"),"encodedPolyline")),
                    BoardLocation=StopLocation(Child(stops,"departureStop")),AlightLocation=StopLocation(Child(stops,"arrivalStop"))
                });
            }}
            if(walk>maxWalkMeters)continue;
            var duration=Text(route,"duration")?.TrimEnd('s');
            if(!double.TryParse(duration,System.Globalization.CultureInfo.InvariantCulture,out var seconds)||!double.IsFinite(seconds)||seconds<0||seconds>int.MaxValue)continue;
            string? fare=null;var money=Child(Child(route,"travelAdvisory"),"transitFare");
            if(Text(money,"currencyCode") is {} currency&&decimal.TryParse(Text(money,"units")??"0",System.Globalization.NumberStyles.Integer,System.Globalization.CultureInfo.InvariantCulture,out var units))fare=$"{currency} {units+Number(money,"nanos")/1_000_000_000m:0.00} · estimativa do provedor";
            var encoded=Text(Child(route,"polyline"),"encodedPolyline");
            results.Add(new(Text(route,"description")??"Trajeto informado pelo Google",Number(route,"distanceMeters"),(int)seconds,steps,fare,encoded) { Path=RouteGeometry.Decode(encoded) });
        }
        return results;
    }
    private static int? Seconds(string? value) => value is not null && value.EndsWith('s') && double.TryParse(value[..^1],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var seconds) && double.IsFinite(seconds) && seconds is >=0 and <=int.MaxValue ? (int)seconds : null;
    private static RouteCoordinate? StopLocation(JsonElement stop) {
        if(stop.ValueKind!=JsonValueKind.Object||!stop.TryGetProperty("location",out var location)||location.ValueKind!=JsonValueKind.Object||!location.TryGetProperty("latLng",out var point)||point.ValueKind!=JsonValueKind.Object
            ||!point.TryGetProperty("latitude",out var latitude)||!point.TryGetProperty("longitude",out var longitude)
            ||latitude.ValueKind!=JsonValueKind.Number||longitude.ValueKind!=JsonValueKind.Number||!latitude.TryGetDouble(out var lat)||!longitude.TryGetDouble(out var lng)||!CatalogMapLayout.IsPlottable(lat,lng))return null;
        return new(lat,lng);
    }
    private static object Waypoint(string address) {
        var parts=address.Split(',');
        if(parts.Length==2&&double.TryParse(parts[0],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var lat)
            &&double.TryParse(parts[1],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var lng)&&CatalogMapLayout.IsPlottable(lat,lng))
            return new {location=new {latLng=new {latitude=lat,longitude=lng}}};
        return new {address};
    }
    private async Task<bool> ReserveAsync(CancellationToken ct)
    {
        await using var db=await factory.OpenAsync(ct);await using var tx=await db.BeginTransactionAsync(ct);
        await db.ExecuteAsync(new CommandDefinition("INSERT IGNORE INTO mobility_api_usage(usage_day,provider) VALUES(UTC_DATE(),'google_routes')",transaction:tx,cancellationToken:ct));
        var changed=await db.ExecuteAsync(new CommandDefinition("UPDATE mobility_api_usage SET requests=requests+1,last_used_at=UTC_TIMESTAMP(6) WHERE usage_day=UTC_DATE() AND provider='google_routes' AND requests<@limit",new{limit=Math.Clamp(config.GetValue<int>("GoogleRoutes:DailyRequestLimit"),0,10000)},tx,cancellationToken:ct));
        await tx.CommitAsync(ct);return changed==1;
    }
    private async Task FailAsync(string error,CancellationToken ct)
    {await using var db=await factory.OpenAsync(ct);await db.ExecuteAsync(new CommandDefinition("UPDATE mobility_api_usage SET failures=failures+1,last_error=@error WHERE usage_day=UTC_DATE() AND provider='google_routes'",new{error},cancellationToken:ct));}
}
