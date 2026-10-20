using Cleaner.Core.Localization;

namespace Cleaner.UI.Theming;

/// <summary>
/// Valittava fonttiyhdistelmä: otsikot, leipäteksti ja numerot. Arvot ovat FontFamily-merkkijonoja
/// (pilkulla erotettu varalista tai ms-appx-polku muotoa "tiedosto#perhe").
/// </summary>
public sealed record FontChoice(string Id, LocalizedText Name, string HeadingFamily, string BodyFamily, string NumberFamily)
{
    /// <summary>Lokin tasalevyinen fontti, sama kaikissa valinnoissa.</summary>
    public const string MonoFamily = "Cascadia Mono, Consolas";
}
