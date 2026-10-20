using Cleaner.Core.Services;
using Cleaner.Modules.Uninstall;

namespace Cleaner.UI.ViewModels;

/// <summary>Yksi rivi Ohjelman poisto -sivun ohjelmalistassa.</summary>
public sealed class InstalledProgramViewModel
{
    public InstalledProgram Program { get; }

    public string Name => Program.Name;
    public string Version => Program.Version ?? "";
    public string Publisher => Program.Publisher ?? "";
    public string SizeText => Program.EstimatedSizeBytes > 0 ? ByteFormatter.Format(Program.EstimatedSizeBytes) : "";
    public string InstallDateText => Program.InstallDate?.ToString("d.M.yyyy") ?? "";

    public InstalledProgramViewModel(InstalledProgram program)
    {
        Program = program;
    }
}
