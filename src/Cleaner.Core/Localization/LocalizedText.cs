namespace Cleaner.Core.Localization;

/// <summary>
/// Kaksikielinen teksti, joka valitsee kielen vasta näytettäessä (<see cref="ToString"/>).
/// Implisiittinen muunnos merkkijonosta käyttää samaa tekstiä molemmilla kielillä.
/// </summary>
public readonly record struct LocalizedText(string En, string Fi)
{
    public static implicit operator LocalizedText(string text) => new(text, text);

    public override string ToString() => Loc.T(En, Fi);
}
