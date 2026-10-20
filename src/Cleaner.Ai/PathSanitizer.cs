using System.Text.RegularExpressions;

namespace Cleaner.Ai;

/// <summary>
/// Piilottaa käyttäjänimen mallille lähetettävästä tekstistä: nykyisen profiilin polku → <c>%USERPROFILE%</c>
/// ja muualla esiintyvä <c>\Users\&lt;nimi&gt;\</c> → <c>\Users\%USERNAME%\</c>, kirjainkoosta riippumatta.
/// Muiden käyttäjien profiilit ja etuliitteen jakavat nimet (<c>\Users\villeneuve\</c>) eivät muutu.
/// </summary>
public sealed class PathSanitizer
{
    private readonly Regex? _profile;
    private readonly Regex? _userSegment;

    public PathSanitizer(string userProfile, string userName)
    {
        var profile = (userProfile ?? "").TrimEnd('\\');
        if (profile.Length > 0)
        {
            // Osuma hyväksytään vain, kun sitä ei seuraa kirjain, numero, _, - tai . (eli seuraa \, loppu tai muu merkki).
            _profile = new Regex(Regex.Escape(profile) + @"(?![\w.-])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        if (!string.IsNullOrEmpty(userName))
        {
            _userSegment = new Regex(@"\\Users\\" + Regex.Escape(userName) + @"\\",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
    }

    /// <summary>Profiilipolku <c>Environment.SpecialFolder.UserProfile</c> ja käyttäjänimi <c>Environment.UserName</c>.</summary>
    public static PathSanitizer ForCurrentUser() =>
        new(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.UserName);

    public string Sanitize(string text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        if (_profile is not null) text = _profile.Replace(text, "%USERPROFILE%");
        if (_userSegment is not null) text = _userSegment.Replace(text, @"\Users\%USERNAME%\");
        return text;
    }
}
