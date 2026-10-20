namespace Cleaner.Ai;

/// <summary>Avustajan virhe. Viesti on englanniksi; käyttöliittymä näyttää lokalisoidun tekstin <see cref="Kind"/>-arvon mukaan.</summary>
public sealed class AiException : Exception
{
    public AiErrorKind Kind { get; }

    /// <summary>Kieltäytymisen luokka (<see cref="AiErrorKind.Refused"/>), muuten null.</summary>
    public string? Category { get; }

    public AiException(AiErrorKind kind, string message, Exception? inner = null, string? category = null)
        : base(message, inner)
    {
        Kind = kind;
        Category = category;
    }
}
