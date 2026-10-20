using Cleaner.Core.Localization;
using Windows.UI;

namespace Cleaner.UI.Theming;

/// <summary>
/// Paletin 14 väriä yhdessä tilassa (tumma tai vaalea).
/// <see cref="PaneBackground"/> on puoliläpinäkyvä (alpha 0xB3), jotta ikkunan taustamateriaali näkyy läpi.
/// </summary>
public sealed record PaletteColors(
    Color Accent, Color AccentHover, Color AccentPressed, Color OnAccent, Color AccentText,
    Color PaneBackground, Color ContentBackground, Color CardBackground, Color CardBorder, Color CardHover,
    Color TextPrimary, Color TextSecondary, Color TextTertiary, Color Divider);

/// <summary>
/// Valittava väripaletti: tunniste (asetuksiin ja kuvaketiedoston nimeen), näyttönimi,
/// vaalean ja tumman tilan värit sekä treemapin kategoriavärit (samat molemmissa tiloissa).
/// </summary>
public sealed record Palette(string Id, LocalizedText Name, PaletteColors Light, PaletteColors Dark, IReadOnlyList<Color> Treemap);
