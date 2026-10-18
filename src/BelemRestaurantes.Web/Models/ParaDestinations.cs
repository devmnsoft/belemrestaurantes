namespace BelemRestaurantes.Web.Models;

public sealed record ParaDestination(string City, string RegionKey, string DescriptionKey, string Icon, string Tone);

public static class ParaDestinations
{
    public static IReadOnlyList<ParaDestination> Featured { get; } =
    [
        new("Belém","DestinationBelemRegion","DestinationBelemText","market","terracotta"),
        new("Santarém","DestinationSantaremRegion","DestinationSantaremText","river","river"),
        new("Soure","DestinationSoureRegion","DestinationSoureText","leaf","forest"),
        new("Salinópolis","DestinationSalinasRegion","DestinationSalinasText","fish","sand"),
        new("Marabá","DestinationMarabaRegion","DestinationMarabaText","river","acai"),
        new("Altamira","DestinationAltamiraRegion","DestinationAltamiraText","cuia","forest")
    ];
}
