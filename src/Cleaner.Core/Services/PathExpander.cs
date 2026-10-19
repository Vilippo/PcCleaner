namespace Cleaner.Core.Services;

/// <summary>
/// Laajentaa sääntöjen polut todellisiksi kansioiksi:
/// ympäristömuuttujat (%TEMP%) ja yksi jokeri (*) kansiotasolla.
/// </summary>
public static class PathExpander
{
    /// <summary>
    /// Palauttaa kaikki olemassa olevat kansiot, joihin mallipolku täsmää.
    /// Esim. "%LOCALAPPDATA%\Google\Chrome\User Data\*\Cache" → yksi polku per profiili.
    /// </summary>
    public static IEnumerable<string> Expand(string template)
    {
        var expanded = Environment.ExpandEnvironmentVariables(template);

        // Ei jokeria: palauta sellaisenaan jos olemassa.
        if (!expanded.Contains('*'))
        {
            if (Directory.Exists(expanded))
                yield return Path.GetFullPath(expanded);
            yield break;
        }

        // Jaa polku osiin ja etsi jokeria sisältävä osa.
        var parts = expanded.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        var starIndex = Array.FindIndex(parts, p => p.Contains('*'));

        // Juuri (esim. "C:") ja polku ennen jokeria.
        var root = parts[0].EndsWith(':') ? parts[0] + Path.DirectorySeparatorChar : parts[0];
        var prefix = parts.Length > 1
            ? Path.Combine(new[] { root }.Concat(parts.Skip(1).Take(starIndex - 1)).ToArray())
            : root;

        if (!Directory.Exists(prefix))
            yield break;

        var suffix = parts.Skip(starIndex + 1).ToArray();

        IEnumerable<string> matches;
        try
        {
            matches = Directory.EnumerateDirectories(prefix, parts[starIndex]);
        }
        catch (UnauthorizedAccessException) { yield break; }
        catch (IOException) { yield break; }

        foreach (var dir in matches)
        {
            var full = suffix.Length == 0 ? dir : Path.Combine(new[] { dir }.Concat(suffix).ToArray());
            if (Directory.Exists(full))
                yield return Path.GetFullPath(full);
        }
    }
}
