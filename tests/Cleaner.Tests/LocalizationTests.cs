using Cleaner.Core.Localization;
using Cleaner.Core.Rules;
using Cleaner.Core.Services;
using Xunit;

namespace Cleaner.Tests;

/// <summary>
/// Testaa kielivalintaa, kaksikielistä tekstiä, tavumäärän muotoilua, asetustiedostoa ja säännön näyttönimeä.
/// Asetustiedostot kirjoitetaan väliaikaiseen kansioon %TEMP%\pccleaner-tests\&lt;guid&gt;.
/// </summary>
[Collection(TestLanguage.Collection)]
public sealed class LocalizationTests : IDisposable
{
    private readonly string _root;

    public LocalizationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "pccleaner-tests", Guid.NewGuid().ToString("N"));
        Loc.Current = Language.En;
    }

    public void Dispose()
    {
        Loc.Current = Language.Fi;
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private static CleanRule Rule(string? nameFi = null, string? descriptionFi = null) => new()
    {
        Id = "test",
        Name = "Temporary files",
        NameFi = nameFi,
        Description = "Safe to delete.",
        DescriptionFi = descriptionFi,
        Paths = new() { "%TEMP%" }
    };

    [Fact]
    public void T_returns_text_for_current_language()
    {
        Assert.Equal("Cancel", Loc.T("Cancel", "Peruuta"));
        Assert.Equal("en-US", Loc.Culture.Name);

        Loc.Current = Language.Fi;

        Assert.Equal("Peruuta", Loc.T("Cancel", "Peruuta"));
        Assert.Equal("fi-FI", Loc.Culture.Name);
    }

    [Fact]
    public void Changed_fires_once_per_real_change()
    {
        int count = 0;
        EventHandler handler = (_, _) => count++;
        Loc.Changed += handler;
        try
        {
            Loc.Current = Language.En;   // sama arvo
            Assert.Equal(0, count);

            Loc.Current = Language.Fi;
            Assert.Equal(1, count);

            Loc.Current = Language.Fi;   // sama arvo
            Assert.Equal(1, count);

            Loc.Current = Language.En;
            Assert.Equal(2, count);
        }
        finally
        {
            Loc.Changed -= handler;
        }
    }

    [Theory]
    [InlineData("EN", true, Language.En)]
    [InlineData(" fi ", true, Language.Fi)]
    [InlineData("fi", true, Language.Fi)]
    [InlineData("sv", false, Language.En)]
    [InlineData(null, false, Language.En)]
    public void TryParse_accepts_en_and_fi_only(string? code, bool expected, Language expectedLanguage)
    {
        Assert.Equal(expected, Loc.TryParse(code, out var language));
        Assert.Equal(expectedLanguage, language);
    }

    [Fact]
    public void ToCode_returns_two_letter_code()
    {
        Assert.Equal("en", Loc.ToCode(Language.En));
        Assert.Equal("fi", Loc.ToCode(Language.Fi));
    }

    [Fact]
    public void LocalizedText_follows_current_language()
    {
        var text = new LocalizedText("Settings", "Asetukset");
        LocalizedText same = "Windows";

        Assert.Equal("Settings", text.ToString());
        Assert.Equal("Windows", same.ToString());

        Loc.Current = Language.Fi;

        Assert.Equal("Asetukset", text.ToString());
        Assert.Equal("Windows", same.ToString());
        Assert.Equal(new LocalizedText("Windows", "Windows"), same);
    }

    [Theory]
    [InlineData(0L, "0 B", "0 t")]
    [InlineData(512L, "512 B", "512 t")]
    [InlineData(1_536_000L, "1.46 MB", "1,46 Mt")]
    public void ByteFormatter_uses_language_units_and_culture(long bytes, string en, string fi)
    {
        Assert.Equal(en, ByteFormatter.Format(bytes));

        Loc.Current = Language.Fi;

        Assert.Equal(fi, ByteFormatter.Format(bytes));
    }

    [Fact]
    public void AppSettings_save_and_load_round_trip()
    {
        var path = Path.Combine(_root, "sub", "settings.json");

        new AppSettings { Language = "fi" }.Save(path);   // luo kansion
        var loaded = AppSettings.Load(path);

        Assert.Equal("fi", loaded.Language);
        Assert.Equal(Language.Fi, loaded.ResolveLanguage());
        Assert.Contains("\"language\": \"fi\"", File.ReadAllText(path));
    }

    [Fact]
    public void AppSettings_missing_file_gives_english_without_writing()
    {
        var path = Path.Combine(_root, "settings.json");

        var loaded = AppSettings.Load(path);

        Assert.Equal("en", loaded.Language);
        Assert.Equal(Language.En, loaded.ResolveLanguage());
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void AppSettings_broken_json_gives_english()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "settings.json");
        File.WriteAllText(path, "{ \"language\": ");

        var loaded = AppSettings.Load(path);

        Assert.Equal("en", loaded.Language);
        Assert.Equal(Language.En, loaded.ResolveLanguage());
    }

    [Fact]
    public void AppSettings_unknown_language_resolves_to_english()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "settings.json");
        File.WriteAllText(path, "{ \"language\": \"sv\" }");

        var loaded = AppSettings.Load(path);

        Assert.Equal("sv", loaded.Language);
        Assert.Equal(Language.En, loaded.ResolveLanguage());
    }

    [Fact]
    public void CleanRule_display_texts_follow_language()
    {
        var rule = Rule(nameFi: "Väliaikaistiedostot", descriptionFi: "Turvallista poistaa.");

        Assert.Equal("Temporary files", rule.DisplayName);
        Assert.Equal("Safe to delete.", rule.DisplayDescription);

        Loc.Current = Language.Fi;

        Assert.Equal("Väliaikaistiedostot", rule.DisplayName);
        Assert.Equal("Turvallista poistaa.", rule.DisplayDescription);
    }

    [Fact]
    public void CleanRule_without_finnish_falls_back_to_english()
    {
        var rule = Rule();
        Loc.Current = Language.Fi;

        Assert.Equal("Temporary files", rule.DisplayName);
        Assert.Equal("Safe to delete.", rule.DisplayDescription);
    }

    [Fact]
    public void RuleLoader_reads_finnish_fields()
    {
        var set = RuleLoader.LoadFromJson("""
            { "rules": [ { "id": "t", "name": "Temp", "nameFi": "Väliaikaiset",
                           "description": "D", "descriptionFi": "K", "paths": ["%TEMP%"] } ] }
            """);

        var rule = Assert.Single(set.Rules);
        Assert.Equal("Väliaikaiset", rule.NameFi);
        Assert.Equal("K", rule.DescriptionFi);
        Assert.Equal("Temp", rule.DisplayName);
    }
}
