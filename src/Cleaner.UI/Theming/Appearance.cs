using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Cleaner.UI.Theming;

/// <summary>
/// Sovelluksen paletit ja fontit sekä niistä rakennettava resurssisanakirja.
/// Kaikki näkymien värit ja fontit haetaan <c>{ThemeResource}</c>-viittauksina tämän luokan avaimiin.
/// </summary>
public static class Appearance
{
    // Riskivärit ovat samat kaikille paleteille, vain tila vaihtelee.
    private sealed record RiskColors(
        Color LowFill, Color LowTint, Color LowText,
        Color MediumFill, Color MediumTint, Color MediumText,
        Color HighFill, Color HighTint, Color HighText);

    private static readonly RiskColors DarkRisk = new(
        H("#97C459"), H("#27500A"), H("#C0DD97"),
        H("#EF9F27"), H("#633806"), H("#FAC775"),
        H("#F09595"), H("#791F1F"), H("#F7C1C1"));

    private static readonly RiskColors LightRisk = new(
        H("#639922"), H("#EAF3DE"), H("#27500A"),
        H("#EF9F27"), H("#FAEEDA"), H("#633806"),
        H("#E24B4A"), H("#FCEBEB"), H("#791F1F"));

    /// <summary>Paletit Scales-järjestyksessä: mint, indigo, steel, graphite, sun. Ensimmäinen on oletus.</summary>
    public static IReadOnlyList<Palette> Palettes { get; } =
    [
        new Palette("mint", new("Mint", "Minttu"),
            Light: P("#0F6E56", "#138163", "#0B5A46", "#FFFFFF", "#0F6E56", "#B3E9F1EC", "#F6F9F7", "#FFFFFF", "#D5E2DC", "#EEF5F1", "#0F1F1A", "#4C6B61", "#7E978E", "#D5E2DC"),
            Dark: P("#1D9E75", "#2FB388", "#178A66", "#04342C", "#5DCAA5", "#B30E1716", "#152120", "#1A2A27", "#243733", "#1F302C", "#E6F0ED", "#9FB8B0", "#6B8580", "#223330"),
            Treemap: T("#1D9E75", "#378ADD", "#7F77DD", "#EF9F27", "#D4537E", "#888780")),
        new Palette("indigo", new("Indigo", "Indigo"),
            Light: P("#534AB7", "#5F56C4", "#453D9E", "#FFFFFF", "#534AB7", "#B3ECEBF7", "#F7F7FB", "#FFFFFF", "#DBDAEC", "#F0EFF8", "#16152B", "#55547A", "#8685A3", "#DBDAEC"),
            Dark: P("#7F77DD", "#928BE3", "#6E66CF", "#1A1540", "#AFA9EC", "#B3121320", "#1A1B2C", "#22233A", "#2E2F48", "#282A42", "#E9E8F5", "#A9A7C8", "#6E6D8C", "#2A2B42"),
            Treemap: T("#7F77DD", "#1D9E75", "#378ADD", "#EF9F27", "#D4537E", "#888780")),
        new Palette("steel", new("Steel", "Teräs"),
            Light: P("#185FA5", "#1C6BB8", "#134E88", "#FFFFFF", "#185FA5", "#B3E8EEF5", "#F5F8FB", "#FFFFFF", "#D6DFE9", "#EDF2F7", "#0E1A26", "#4A5D72", "#7B8CA0", "#D6DFE9"),
            Dark: P("#378ADD", "#4E99E3", "#2C7AC8", "#042C53", "#85B7EB", "#B30F161F", "#16202C", "#1C2938", "#273646", "#223141", "#E6EDF5", "#9FB1C6", "#5F7389", "#223040"),
            Treemap: T("#378ADD", "#1D9E75", "#7F77DD", "#EF9F27", "#D4537E", "#888780")),
        new Palette("graphite", new("Graphite", "Grafiitti"),
            Light: P("#1C1C1A", "#2C2C2A", "#000000", "#FFFFFF", "#1C1C1A", "#B3EDECE8", "#F7F6F3", "#FFFFFF", "#DCDAD3", "#F1EFE8", "#141414", "#5F5E5A", "#8A8984", "#DCDAD3"),
            Dark: P("#F1EFE8", "#FFFFFF", "#D3D1C7", "#141414", "#F1EFE8", "#B3101010", "#181818", "#202020", "#2E2E2E", "#262626", "#F1EFE8", "#A8A69E", "#6E6D68", "#2A2A2A"),
            Treemap: T("#B4B2A9", "#5F5E5A", "#D3D1C7", "#888780", "#444441", "#F1EFE8")),
        new Palette("sun", new("Sun", "Aurinko"),
            Light: P("#B8471F", "#C4532B", "#993C1D", "#FFFFFF", "#993C1D", "#B3F1E9DD", "#F8F3EB", "#FFFFFF", "#E3D8C8", "#F3ECE0", "#2A1F16", "#6B5A4B", "#9B8A7A", "#E3D8C8"),
            Dark: P("#E07A52", "#E98B66", "#D2683F", "#2B1208", "#F0997B", "#B31E1915", "#26201B", "#2E2721", "#3C332C", "#352D26", "#F3EDE6", "#BDAE9E", "#85776A", "#3A312A"),
            Treemap: T("#D85A30", "#1D9E75", "#378ADD", "#7F77DD", "#EF9F27", "#888780")),
    ];

    /// <summary>Fontit Scales-järjestyksessä: bahnschrift, segoe, inter. Ensimmäinen on oletus.</summary>
    public static IReadOnlyList<FontChoice> Fonts { get; } =
    [
        new FontChoice("bahnschrift", new("Bahnschrift and Segoe UI", "Bahnschrift ja Segoe UI"),
            HeadingFamily: "Bahnschrift",
            BodyFamily: "Segoe UI Variable Text, Segoe UI",
            NumberFamily: "Bahnschrift"),
        new FontChoice("segoe", new("Segoe UI Variable", "Segoe UI Variable"),
            HeadingFamily: "Segoe UI Variable Display, Segoe UI",
            BodyFamily: "Segoe UI Variable Text, Segoe UI",
            NumberFamily: "Segoe UI Variable Text, Segoe UI"),
        new FontChoice("inter", new("Inter", "Inter"),
            HeadingFamily: InterFamily,
            BodyFamily: InterFamily,
            NumberFamily: InterFamily),
    ];

    private const string InterFamily = "ms-appx:///Assets/Fonts/InterVariable.ttf#Inter";

    public static Palette DefaultPalette => Palettes[0];

    public static FontChoice DefaultFont => Fonts[0];

    /// <summary>Viimeksi <see cref="Apply"/>-kutsulla asetettu paletti; ennen ensimmäistä kutsua oletus.</summary>
    public static Palette CurrentPalette { get; private set; } = DefaultPalette;

    /// <summary>Viimeksi <see cref="Apply"/>-kutsulla asetettu fontti; ennen ensimmäistä kutsua oletus.</summary>
    public static FontChoice CurrentFont { get; private set; } = DefaultFont;

    // Edellisen Apply-kutsun lisäämä sanakirja, poistetaan ennen uuden lisäämistä.
    private static ResourceDictionary? _applied;

    /// <summary>Hakee paletin tunnisteella (kirjainkoko ja reunavälit ohitetaan). Tuntematon, tyhjä tai null antaa oletuksen.</summary>
    public static Palette ResolvePalette(string? id)
    {
        var key = id?.Trim();
        if (string.IsNullOrEmpty(key)) return DefaultPalette;
        return Palettes.FirstOrDefault(p => string.Equals(p.Id, key, StringComparison.OrdinalIgnoreCase)) ?? DefaultPalette;
    }

    /// <summary>Hakee fontin tunnisteella (kirjainkoko ja reunavälit ohitetaan). Tuntematon, tyhjä tai null antaa oletuksen.</summary>
    public static FontChoice ResolveFont(string? id)
    {
        var key = id?.Trim();
        if (string.IsNullOrEmpty(key)) return DefaultFont;
        return Fonts.FirstOrDefault(f => string.Equals(f.Id, key, StringComparison.OrdinalIgnoreCase)) ?? DefaultFont;
    }

    /// <summary>Paletin ikkuna- ja exe-kuvakkeen tiedostonimi Assets-kansiossa.</summary>
    public static string IconFileName(Palette palette) => $"PcCleaner-{palette.Id}.ico";

    /// <summary>
    /// Rakentaa paletin ja fontin resurssisanakirjan ja vaihtaa sen sovellusresursseihin edellisen tilalle.
    /// Kutsutaan vain UI-säikeestä. Jo luodut näkymät eivät päivity itsestään (näkymän uudelleenluonti on kutsujan asia).
    /// </summary>
    public static void Apply(Palette palette, FontChoice font)
    {
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(font);

        var dictionary = new ResourceDictionary();
        dictionary.ThemeDictionaries["Default"] = BuildTheme(palette.Dark, DarkRisk, font);
        dictionary.ThemeDictionaries["Light"] = BuildTheme(palette.Light, LightRisk, font);
        // Ilman omaa HighContrast-sanakirjaa WinUI käyttäisi suuren kontrastin tilassa Default-sanakirjaa,
        // ja paletin korostusohitukset peittäisivät järjestelmän värit.
        dictionary.ThemeDictionaries["HighContrast"] = BuildHighContrastTheme(font);

        var merged = Application.Current.Resources.MergedDictionaries;
        if (_applied is not null) merged.Remove(_applied);
        merged.Add(dictionary);
        _applied = dictionary;

        CurrentPalette = palette;
        CurrentFont = font;
    }

    // Tumma tai vaalea teemasanakirja: omat avaimet, fonttiavaimet ja WinUI:n korostusohitukset.
    private static ResourceDictionary BuildTheme(PaletteColors c, RiskColors risk, FontChoice font)
    {
        var d = new ResourceDictionary();

        AddAppColors(d,
            accent: c.Accent, accentHover: c.AccentHover, accentPressed: c.AccentPressed,
            onAccent: c.OnAccent, accentText: c.AccentText,
            pane: c.PaneBackground, content: c.ContentBackground, card: c.CardBackground,
            cardBorder: c.CardBorder, cardHover: c.CardHover,
            textPrimary: c.TextPrimary, textSecondary: c.TextSecondary, textTertiary: c.TextTertiary,
            divider: c.Divider, risk: risk);
        AddFonts(d, font);

        // WinUI:n korostusvärit, joista sisäänrakennetut ohjaimet johtavat omat siveltimensä.
        d["SystemAccentColor"] = c.Accent;
        d["SystemAccentColorLight1"] = c.AccentHover;
        d["SystemAccentColorLight2"] = c.AccentHover;
        d["SystemAccentColorLight3"] = c.AccentHover;
        d["SystemAccentColorDark1"] = c.AccentPressed;
        d["SystemAccentColorDark2"] = c.AccentPressed;
        d["SystemAccentColorDark3"] = c.AccentPressed;

        // WinUI:n korostussiveltimet (Windows App SDK 1.8:n generic.xaml-nimet).
        d["AccentFillColorDefaultBrush"] = B(c.Accent);
        d["AccentFillColorSecondaryBrush"] = B(c.AccentHover);
        d["AccentFillColorTertiaryBrush"] = B(c.AccentPressed);
        d["AccentFillColorDisabledBrush"] = B(WithAlpha(c.Accent, 0x5C));
        d["AccentTextFillColorPrimaryBrush"] = B(c.AccentText);
        d["AccentTextFillColorSecondaryBrush"] = B(c.AccentText);
        d["AccentTextFillColorTertiaryBrush"] = B(c.AccentText);
        d["TextOnAccentFillColorPrimaryBrush"] = B(c.OnAccent);
        d["TextOnAccentFillColorSecondaryBrush"] = B(WithAlpha(c.OnAccent, 0xB3));
        d["AccentFillColorSelectedTextBackgroundBrush"] = B(c.Accent);
        d["NavigationViewSelectionIndicatorForeground"] = B(c.Accent);
        d["SystemControlHighlightAccentBrush"] = B(c.Accent);
        d["SystemFillColorAttentionBrush"] = B(c.AccentText);
        d["NavigationViewDefaultPaneBackground"] = B(c.PaneBackground);
        d["NavigationViewExpandedPaneBackground"] = B(c.PaneBackground);
        d["NavigationViewTopPaneBackground"] = B(c.PaneBackground);
        d["ExpanderHeaderBackground"] = B(Transparent);
        d["ExpanderContentBackground"] = B(Transparent);
        d["ExpanderHeaderBorderBrush"] = B(Transparent);
        d["ExpanderContentBorderBrush"] = B(Transparent);
        d["ExpanderContentDownBorderThickness"] = new Thickness(0);

        return d;
    }

    // Suuren kontrastin teemasanakirja: samat omat avaimet järjestelmäväreistä, ei WinUI:n korostusohituksia.
    // Arvot luetaan Apply-hetkellä.
    private static ResourceDictionary BuildHighContrastTheme(FontChoice font)
    {
        var settings = new UISettings();
        var accent = settings.GetColorValue(UIColorType.Accent);
        var background = settings.GetColorValue(UIColorType.Background);
        var foreground = settings.GetColorValue(UIColorType.Foreground);

        var d = new ResourceDictionary();
        AddAppColors(d,
            accent: accent, accentHover: accent, accentPressed: accent,
            onAccent: background, accentText: accent,
            pane: background, content: background, card: background,
            cardBorder: foreground, cardHover: background,
            textPrimary: foreground, textSecondary: foreground, textTertiary: foreground,
            divider: foreground,
            risk: new RiskColors(
                foreground, background, foreground,
                foreground, background, foreground,
                foreground, background, foreground));
        AddFonts(d, font);
        return d;
    }

    // Omat App*- ja Risk*-avaimet sekä siveltiminä (…Brush) että väreinä (…Color).
    private static void AddAppColors(ResourceDictionary d,
        Color accent, Color accentHover, Color accentPressed, Color onAccent, Color accentText,
        Color pane, Color content, Color card, Color cardBorder, Color cardHover,
        Color textPrimary, Color textSecondary, Color textTertiary, Color divider, RiskColors risk)
    {
        Add(d, "AppAccent", accent);
        Add(d, "AppAccentHover", accentHover);
        Add(d, "AppAccentPressed", accentPressed);
        Add(d, "AppOnAccent", onAccent);
        Add(d, "AppAccentText", accentText);
        Add(d, "AppPaneBackground", pane);
        Add(d, "AppContentBackground", content);
        Add(d, "AppCardBackground", card);
        Add(d, "AppCardBorder", cardBorder);
        Add(d, "AppCardHover", cardHover);
        Add(d, "AppTextPrimary", textPrimary);
        Add(d, "AppTextSecondary", textSecondary);
        Add(d, "AppTextTertiary", textTertiary);
        Add(d, "AppDivider", divider);
        Add(d, "RiskLow", risk.LowFill);
        Add(d, "RiskLowTint", risk.LowTint);
        Add(d, "RiskLowText", risk.LowText);
        Add(d, "RiskMedium", risk.MediumFill);
        Add(d, "RiskMediumTint", risk.MediumTint);
        Add(d, "RiskMediumText", risk.MediumText);
        Add(d, "RiskHigh", risk.HighFill);
        Add(d, "RiskHighTint", risk.HighTint);
        Add(d, "RiskHighText", risk.HighText);
    }

    private static void Add(ResourceDictionary d, string name, Color color)
    {
        d[name + "Color"] = color;
        d[name + "Brush"] = B(color);
    }

    private static void AddFonts(ResourceDictionary d, FontChoice font)
    {
        d["AppHeadingFontFamily"] = new FontFamily(font.HeadingFamily);
        d["AppBodyFontFamily"] = new FontFamily(font.BodyFamily);
        d["AppNumberFontFamily"] = new FontFamily(font.NumberFamily);
        d["AppMonoFontFamily"] = new FontFamily(FontChoice.MonoFamily);
        d["ContentControlThemeFontFamily"] = new FontFamily(font.BodyFamily);
    }

    private static readonly Color Transparent = Color.FromArgb(0, 0, 0, 0);

    private static SolidColorBrush B(Color color) => new(color);

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    private static PaletteColors P(params string[] hex)
    {
        if (hex.Length != 14) throw new ArgumentException("Paletissa on 14 väriä.", nameof(hex));
        return new PaletteColors(
            H(hex[0]), H(hex[1]), H(hex[2]), H(hex[3]), H(hex[4]),
            H(hex[5]), H(hex[6]), H(hex[7]), H(hex[8]), H(hex[9]),
            H(hex[10]), H(hex[11]), H(hex[12]), H(hex[13]));
    }

    private static IReadOnlyList<Color> T(params string[] hex) => Array.ConvertAll(hex, H);

    // "#RRGGBB" tai "#AARRGGBB" (esim. PaneBackground #B3... = alpha 0xB3).
    private static Color H(string hex)
    {
        var value = uint.Parse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return hex.Length switch
        {
            7 => Color.FromArgb(0xFF, (byte)(value >> 16), (byte)(value >> 8), (byte)value),
            9 => Color.FromArgb((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value),
            _ => throw new FormatException($"Tuntematon värimuoto: {hex}"),
        };
    }
}
