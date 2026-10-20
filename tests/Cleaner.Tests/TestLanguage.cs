using System.Runtime.CompilerServices;
using Cleaner.Core.Localization;
using Xunit;

namespace Cleaner.Tests;

/// <summary>
/// Testien kieliasetus. Valtaosa testeistä tarkistaa suomenkielisiä tekstejä, joten kieli asetetaan
/// suomeksi ennen ensimmäistä testiä. Englantia testaavat luokat kuuluvat kokoelmaan
/// <see cref="Collection"/>, asettavat <c>Loc.Current = Language.En</c> ja palauttavat suomen lopuksi.
/// </summary>
[CollectionDefinition(Collection, DisableParallelization = true)]
public sealed class TestLanguage
{
    /// <summary>Loc.Current on staattinen: sitä vaihtavat testiluokat eivät saa ajaa rinnakkain.</summary>
    public const string Collection = "Language";

#pragma warning disable CA2255 // Testiprojektin moduulialustaja on tarkoituksellinen: kieli on asetettava ennen yhtäkään testiä.
    [ModuleInitializer]
    internal static void Initialize() => Loc.Current = Language.Fi;
#pragma warning restore CA2255
}
