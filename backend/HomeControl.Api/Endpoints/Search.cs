namespace HomeControl.Api.Endpoints;

// Hilfen für die Textsuche mit SQL LIKE (EF.Functions.Like).
//
// Einschränkung von SQLite: LIKE ignoriert Groß-/Kleinschreibung nur bei A–Z.
// Umlaute und ß werden nur bei gleicher Schreibweise gefunden ("Küche" findet "Küche",
// aber "KÜCHE" findet "Küche" nicht).
internal static class Search
{
    // Mit diesem Zeichen werden %, _ und \ im Suchtext maskiert.
    public const string EscapeChar = "\\";

    // Macht aus einem Suchtext ein LIKE-Muster für "enthält", z. B. "Bad" -> "%Bad%".
    // %, _ und \ würden sonst als Platzhalter wirken und werden deshalb wörtlich gesucht.
    public static string ContainsPattern(string text)
    {
        var escaped = text
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");

        return $"%{escaped}%";
    }
}
