using System.Text.Json;
using System.Text.RegularExpressions;

namespace FamilyAssistant.Core.Shopping;

// Readable names for abbreviated receipt names ("ĆwiartkaKurczaVac kg" -> "Ćwiartka z kurczaka (na wagę)").
// The dictionary lives in config/product-names.json (key: receipt name in upper case) and is reloaded when it changes.
// A name set by the user via "Zmień nazwę" is never in the dictionary, so it is shown unchanged.
public sealed class ProductNames(IConfiguration config)
{
    private readonly object sync = new();
    private Dictionary<string, string> names = new();
    private DateTime loadedAt = DateTime.MinValue;
    private string Path => config["Shopping:ProductNamesPath"] ?? "/app/config/product-names.json";

    public string Display(string name)
    {
        var map = Current();
        if (map.TryGetValue(name.ToUpperInvariant(), out var readable)) return readable;
        return Humanize(name);
    }

    // Fallback for products not yet in the dictionary: split glued words and units.
    public static string Humanize(string name)
    {
        var text = Regex.Replace(name, @"(?<=\p{Ll})(?=\p{Lu})", " ");
        text = Regex.Replace(text, @"(?<=\p{L})(?=\d)", " ");
        text = Regex.Replace(text, @"(?<=\d)\s*(kg|g|ml|l|szt)\b", " $1", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\b(Luz|LUZ|luz)\b", "(na wagę)");
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    private Dictionary<string, string> Current()
    {
        lock (sync)
        {
            try
            {
                var stamp = File.Exists(Path) ? File.GetLastWriteTimeUtc(Path) : DateTime.MinValue;
                if (stamp != loadedAt)
                {
                    names = stamp == DateTime.MinValue ? new() :
                        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path)) ?? new();
                    loadedAt = stamp;
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
            return names;
        }
    }
}
