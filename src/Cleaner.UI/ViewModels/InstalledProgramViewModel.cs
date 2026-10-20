using Cleaner.Core.Localization;
using Cleaner.Core.Services;
using Cleaner.Modules.Uninstall;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cleaner.UI.ViewModels;

/// <summary>Yksi rivi Ohjelman poisto -sivun ohjelmalistassa.</summary>
public sealed partial class InstalledProgramViewModel : ObservableObject
{
    public InstalledProgram Program { get; }

    public string Name => Program.Name;
    public string Version => Program.Version ?? "";
    public string Publisher => Program.Publisher ?? "";
    public string SizeText => Program.EstimatedSizeBytes > 0 ? ByteFormatter.Format(Program.EstimatedSizeBytes) : "";
    public string InstallDateText => Program.InstallDate?.ToString(Loc.T("yyyy-MM-dd", "d.M.yyyy"), Loc.Culture) ?? "";

    /// <summary>Asennuspäivän työkaluvihje. Tulee tästä, koska DataTemplate ei näe MainViewModelin tekstejä.</summary>
    public string InstallDateToolTip => Loc.T("Install date", "Asennuspäivä");

    public InstalledProgramViewModel(InstalledProgram program)
    {
        Program = program;
    }

    /// <summary>Kielen vaihto: päivämäärä, koko ja työkaluvihje luetaan uudelleen.</summary>
    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(InstallDateText));
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(InstallDateToolTip));
    }
}
