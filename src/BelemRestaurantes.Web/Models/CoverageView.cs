using BelemRestaurantes.Domain.Data;

namespace BelemRestaurantes.Web.Models;

public sealed class CoverageCounts
{
    public string CityKey { get; init; }="";
    public int Total { get; init; }
    public int Published { get; init; }
    public int Pending { get; init; }
    public int MissingAddress { get; init; }
    public int MissingCoordinates { get; init; }
}
public sealed record CoverageCity(ParaMunicipalityRecord Municipality,CoverageCounts Counts);
public sealed class CoverageCategory
{
    public string Name { get; init; }="";
    public int Total { get; init; }
    public int Published { get; init; }
}
public sealed record CoverageView(IReadOnlyList<CoverageCity> AllCities,IReadOnlyList<CoverageCity> Cities,
    IReadOnlyList<CoverageCategory> Categories,string? Region,string? Query,int UnknownMunicipality,DateTimeOffset At)
{
    public const int Target=100000;
    public int Total=>AllCities.Sum(city=>city.Counts.Total);
    public int Published=>AllCities.Sum(city=>city.Counts.Published);
    public int Pending=>AllCities.Sum(city=>city.Counts.Pending);
    public int CoveredCities=>AllCities.Count(city=>city.Counts.Total>0);
}
