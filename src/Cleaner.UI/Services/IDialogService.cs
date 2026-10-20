namespace Cleaner.UI.Services;

/// <summary>ViewModel kysyy vahvistukset tämän kautta, jotta se ei riipu ikkunasta.</summary>
public interface IDialogService
{
    /// <summary>
    /// Kyllä/ei-dialogi. Palauttaa true, kun käyttäjä painaa ensisijaista nappia.
    /// <paramref name="closeText"/> null = Peruuta-teksti nykyisellä kielellä (ratkaistaan toteutuksessa).
    /// </summary>
    Task<bool> ConfirmAsync(string title, string message, string primaryText, string? closeText = null);

    /// <summary>Pelkkä ilmoitus yhdellä sulkunapilla.</summary>
    Task InfoAsync(string title, string message);

    /// <summary>Valinta listasta. Palauttaa valitun vaihtoehdon tai null, jos käyttäjä perui.</summary>
    Task<string?> PickAsync(string title, string message, IReadOnlyList<string> options, string primaryText);
}
