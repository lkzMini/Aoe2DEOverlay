using System.IO;

namespace Aoe2DEOverlay;

public sealed record CivilizationMarker(string Name, string Code, string? ImagePath)
{
    public bool HasEmblem => ImagePath is not null;

    public static CivilizationMarker From(string? name)
    {
        var normalized = name?.Trim() ?? string.Empty;
        if (Aliases.TryGetValue(normalized, out var marker)) return marker;
        var fallback = new string(normalized.Where(char.IsLetter).Take(3).Select(char.ToUpperInvariant).ToArray());
        return new CivilizationMarker(string.IsNullOrWhiteSpace(normalized) ? "Unknown civilization" : normalized,
            string.IsNullOrEmpty(fallback) ? "?" : fallback, null);
    }

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
        for (var i = 0; i < Names.Length; i++)
        {
            var filename = Names[i].ToLowerInvariant();
            if (Names[i] == "Hindustanis") filename = "indians"; // supplied legacy filename
            map[Names[i]] = new CivilizationMarker(Names[i], Codes[i], ImagePathFor(filename));
        }
        map["Bohemians"] = map["Bohemian"];
        map["Indians"] = map["Hindustanis"];
        return map;
    }

    private static string? ImagePathFor(string filename)
    {
        // Images are supplied locally by the project owner. Checking beside the executable
        // means newly added civilization files work without a code change, while missing
        // assets deliberately retain the compact text fallback.
        var localPath = Path.Combine(AppContext.BaseDirectory, "Assets", "images", $"{filename}.png");
        return File.Exists(localPath) ? $"pack://siteoforigin:,,,/Assets/images/{filename}.png" : null;
    }
}
