using CommunityToolkit.Mvvm.ComponentModel;

namespace Cleaner.UI.ViewModels;

/// <summary>Avustajapaneelin viestin tyyppi: määrää taustan ja marginaalit.</summary>
public enum AiMessageRole { User, Assistant, Error, Info }

/// <summary>
/// Yksi viesti avustajapaneelissa. Teksti jää kielelle, jolla se syntyi. Suoratoistossa <see cref="Text"/>
/// kasvaa <see cref="Append"/>-kutsuilla UI-säikeessä.
/// </summary>
public sealed partial class AiMessageViewModel : ObservableObject
{
    public AiMessageRole Role { get; }

    [ObservableProperty]
    public partial string Text { get; set; }

    public bool IsUser => Role == AiMessageRole.User;
    public bool IsAssistant => Role == AiMessageRole.Assistant;
    public bool IsError => Role == AiMessageRole.Error;
    public bool IsInfo => Role == AiMessageRole.Info;

    public AiMessageViewModel(AiMessageRole role, string text)
    {
        Role = role;
        Text = text;
    }

    /// <summary>Text += delta. Kutsutaan UI-säikeessä.</summary>
    public void Append(string delta)
    {
        if (!string.IsNullOrEmpty(delta)) Text += delta;
    }
}
