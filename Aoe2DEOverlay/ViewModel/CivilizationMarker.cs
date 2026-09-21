namespace Aoe2DEOverlay;

public sealed record CivilizationMarker(string Name, string Code, string Geometry, bool HasEmblem)
{
    public static CivilizationMarker From(string? name)
    {
        var normalized = name?.Trim() ?? string.Empty;
        if (Aliases.TryGetValue(normalized, out var marker)) return marker;
        var fallback = new string(normalized.Where(char.IsLetter).Take(3).Select(char.ToUpperInvariant).ToArray());
        return new CivilizationMarker(string.IsNullOrWhiteSpace(normalized) ? "Unknown civilization" : normalized,
            string.IsNullOrEmpty(fallback) ? "?" : fallback, string.Empty, false);
    }

    // Original, abstract vector marks; these are not Age of Empires artwork.
    private static readonly string[] Marks =
    {
        "M12,1 L22,12 12,23 2,12Z", "M3,3 H21 V21 H3Z", "M12,1 L23,22 H1Z",
        "M2,2 H22 V7 H7 V22 H2Z", "M12,1 A11,11 0 1 1 11.9,1Z", "M2,12 L12,2 22,12 12,22Z"
    };
    private static readonly string[] Names =
    {
        "Britons","Franks","Goths","Teutons","Japanese","Chinese","Byzantines","Persians","Saracens","Turks","Vikings","Mongols","Celts","Spanish","Aztecs","Mayans","Huns","Koreans","Italians","Hindustanis","Incas","Magyars","Slavs","Portuguese","Ethiopians","Malians","Berbers","Khmer","Malay","Burmese","Vietnamese","Bulgarians","Tatars","Cumans","Lithuanians","Burgundians","Sicilians","Poles","Bohemian","Dravidians","Bengalis","Gurjaras"
    };
    private static readonly string[] Codes =
    {
        "BRI","FRA","GOT","TEU","JAP","CHI","BYZ","PER","SAR","TUR","VIK","MON","CEL","SPA","AZT","MAY","HUN","KOR","ITA","HIN","INC","MAG","SLA","POR","ETH","MAL","BER","KHM","MLY","BUR","VIE","BUL","TAT","CUM","LIT","BRG","SIC","POL","BOH","DRA","BEN","GUR"
    };
    private static readonly IReadOnlyDictionary<string, CivilizationMarker> Aliases = Build();
    private static IReadOnlyDictionary<string, CivilizationMarker> Build()
    {
        var map = new Dictionary<string, CivilizationMarker>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < Names.Length; i++) map[Names[i]] = new CivilizationMarker(Names[i], Codes[i], Marks[i % Marks.Length], true);
        map["Bohemians"] = map["Bohemian"];
        map["Indians"] = map["Hindustanis"];
        return map;
    }
}
