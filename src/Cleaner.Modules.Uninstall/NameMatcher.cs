using System.Text.RegularExpressions;

namespace Cleaner.Modules.Uninstall;

public enum MatchKind { None, Exact, Tokens, Publisher }

/// <summary>
/// Ohjelman nimen normalisointi sanalistaksi ja kansionimen vertailu siihen ("Matching rules").
/// Vertailut tehdään aina sanalistoina, ei yhdistettyinä merkkijonoina.
/// </summary>
public static class NameMatcher
{
    public static readonly string[] StopWords =
    {
        "the", "for", "and", "windows", "version", "edition", "app", "application", "software",
        "inc", "llc", "ltd", "corp", "corporation", "gmbh", "oy", "ab"
    };

    public static readonly string[] DeniedFolderNames =
    {
        "Microsoft", "Windows", "WindowsApps", "Common Files", "Microsoft Shared", "Packages",
        "Temp", "Programs", "Google", "Intel", "NVIDIA", "AMD"
    };

    private static readonly HashSet<string> StopWordSet = new(StopWords, StringComparer.Ordinal);
    private static readonly HashSet<string> DeniedSet = new(DeniedFolderNames, StringComparer.OrdinalIgnoreCase);

    private static readonly Regex Parentheses = new(@"\([^)]*\)", RegexOptions.CultureInvariant);
    private static readonly Regex DottedVersion = new(@"\d+(\.\d+)+", RegexOptions.CultureInvariant);
    private static readonly Regex Bitness = new(@"(?<![\p{L}\p{N}])(64|32)-bit(?![\p{L}\p{N}])",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex Symbols = new("[™®©]", RegexOptions.CultureInvariant);
    private static readonly Regex Splitter = new(@"[^\p{L}\p{N}]+", RegexOptions.CultureInvariant);
    private static readonly Regex VersionWord = new(@"^v\d+$", RegexOptions.CultureInvariant);

    /// <summary>Normalisoidut sanat: sulkeiden sisältö, versiot ja arkkitehtuurimerkinnät pois, pienet kirjaimet.</summary>
    public static IReadOnlyList<string> Tokens(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return Array.Empty<string>();

        var text = Parentheses.Replace(name, " ");
        text = Symbols.Replace(text, " ");
        text = DottedVersion.Replace(text, " ");
        text = Bitness.Replace(text, " ");
        text = text.ToLowerInvariant();

        return Splitter.Split(text)
            .Where(t => t.Length > 0 && t != "x64" && t != "x86" && !VersionWord.IsMatch(t))
            .ToArray();
    }

    /// <summary>Sanat, joiden pituus on vähintään 3 ja jotka eivät ole kieltolistalla.</summary>
    public static IReadOnlyList<string> SignificantTokens(string name) =>
        Significant(Tokens(name));

    /// <summary>Sanat yhdellä välilyönnillä yhdistettynä (näyttöä ja lokeja varten).</summary>
    public static string Normalize(string name) => string.Join(' ', Tokens(name));

    public static bool IsDeniedFolderName(string folderName) =>
        !string.IsNullOrWhiteSpace(folderName) && DeniedSet.Contains(folderName.Trim());

    /// <summary>Tarkistusjärjestys: kielletty kansio → None, sitten Exact, Publisher, Tokens.</summary>
    public static MatchKind Match(string folderName, string programName, string? publisher)
    {
        if (IsDeniedFolderName(folderName)) return MatchKind.None;

        var folder = Tokens(folderName);
        if (folder.Count == 0) return MatchKind.None;

        var program = Tokens(programName);
        if (program.Count > 0 && folder.SequenceEqual(program)) return MatchKind.Exact;

        if (MatchesPublisher(folder, publisher)) return MatchKind.Publisher;

        var programSig = Significant(program);
        var enoughSignal = programSig.Count >= 2 || (programSig.Count == 1 && programSig[0].Length >= 5);
        if (enoughSignal && programSig.All(folder.Contains)) return MatchKind.Tokens;

        return MatchKind.None;
    }

    private static bool MatchesPublisher(IReadOnlyList<string> folder, string? publisher)
    {
        if (string.IsNullOrWhiteSpace(publisher)) return false;
        var pub = Tokens(publisher);
        if (pub.Count == 0) return false;

        var folderSig = Significant(folder);
        var pubSig = Significant(pub);
        return folderSig.Count == 0 || pubSig.Count == 0
            ? folder.SequenceEqual(pub)
            : folderSig.SequenceEqual(pubSig);
    }

    private static IReadOnlyList<string> Significant(IReadOnlyList<string> tokens) =>
        tokens.Where(t => t.Length >= 3 && !StopWordSet.Contains(t)).ToArray();
}
