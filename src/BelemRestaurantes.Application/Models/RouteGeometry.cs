namespace BelemRestaurantes.Application.Models;

public sealed record RouteCoordinate(double Lat, double Lng);

/// <summary>Google encoded polyline, precision 5. Reject the entire malformed geometry; never connect fabricated endpoints.</summary>
public static class RouteGeometry
{
    public static IReadOnlyList<RouteCoordinate> Decode(string? encoded)
    {
        if(string.IsNullOrEmpty(encoded) || encoded.Length>200000)return [];
        var path=new List<RouteCoordinate>();var index=0;long latitude=0,longitude=0;
        bool Read(out long delta) {
            delta=0;long value=0;var shift=0;
            while(index<encoded.Length && shift<=30) {
                var digit=encoded[index++]-63;
                if(digit is <0 or >63)return false;
                value|=(long)(digit&31)<<shift;
                if(digit<32){delta=(value&1)!=0?~(value>>1):value>>1;return true;}
                shift+=5;
            }
            return false;
        }
        while(index<encoded.Length) {
            if(path.Count>=20000 || !Read(out var lat) || !Read(out var lng))return [];
            latitude+=lat;longitude+=lng;
            if(latitude is <-9000000 or >9000000 || longitude is <-18000000 or >18000000)return [];
            path.Add(new(latitude/100000d,longitude/100000d));
        }
        return path.Count>=2 ? path : [];
    }
}
