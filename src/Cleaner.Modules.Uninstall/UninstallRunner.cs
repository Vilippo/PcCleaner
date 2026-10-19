using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Cleaner.Core.Logging;
using Cleaner.Core.Services;

namespace Cleaner.Modules.Uninstall;

/// <summary>Poisto-ohjelman ajon tulos. Error on null, kun käynnistys onnistui.</summary>
public sealed record UninstallRunResult(int? ExitCode, bool KeyRemoved, TimeSpan Elapsed, string? Error);

/// <summary>
/// Käynnistää ohjelman oman poisto-ohjelman ja odottaa, että sen Uninstall-avain katoaa (REQ-2, EDGE-1, EDGE-10).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class UninstallRunner
{
    private static readonly Regex GuidPattern = new(@"\{[0-9A-Fa-f-]{36}\}", RegexOptions.CultureInvariant);

    private readonly ICleanerLogger _log;
    private readonly TimeSpan _keyRemovalTimeout;
    private readonly TimeSpan _pollInterval;

    public UninstallRunner(ICleanerLogger log, TimeSpan? keyRemovalTimeout = null, TimeSpan? pollInterval = null)
    {
        _log = log;
        _keyRemovalTimeout = keyRemovalTimeout ?? TimeSpan.FromSeconds(60);
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(2);
    }

    /// <summary>Poistokomento (exe, argumentit). Null, jos ohjelmalla ei ole käyttökelpoista komentoa.</summary>
    public static (string FileName, string Arguments)? BuildCommand(InstalledProgram program, bool quiet)
    {
        if (program.IsMsi)
        {
            string? productCode = IsGuidKey(program.KeyName) ? program.KeyName : null;
            if (productCode is null)
            {
                foreach (var cmd in new[] { program.UninstallString, program.QuietUninstallString })
                {
                    if (string.IsNullOrEmpty(cmd)) continue;
                    var m = GuidPattern.Match(cmd);
                    if (m.Success) { productCode = m.Value; break; }
                }
            }
            if (productCode is not null)
                return ("msiexec.exe", "/x " + productCode + (quiet ? " /qb-" : ""));
        }

        var command = quiet && !string.IsNullOrWhiteSpace(program.QuietUninstallString)
            ? program.QuietUninstallString
            : program.UninstallString;

        var exe = UninstallCommandLine.ExtractExecutable(command);
        if (exe is null) return null;
        return (exe, UninstallCommandLine.ExtractArguments(command) ?? "");
    }

    public async Task<UninstallRunResult> RunAsync(InstalledProgram program, bool quiet, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var command = BuildCommand(program, quiet);
        if (command is null)
        {
            const string noCommand = "Ohjelmalla ei ole poisto-ohjelmaa.";
            _log.Warning($"{program.Name}: {noCommand}");
            return new UninstallRunResult(null, !RegistryPaths.KeyExists(program.KeyPath), stopwatch.Elapsed, noCommand);
        }

        var (fileName, arguments) = command.Value;
        _log.Info($"Käynnistetään poisto-ohjelma: {fileName} {arguments}".TrimEnd());

        int exitCode;
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = true
            });
            if (process is null)
            {
                var msg = $"Poisto-ohjelmaa ei voitu käynnistää: {fileName}";
                _log.Warning(msg);
                return new UninstallRunResult(null, false, stopwatch.Elapsed, msg);
            }

            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            exitCode = process.ExitCode;
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException)
        {
            var msg = $"Poisto-ohjelmaa ei voitu käynnistää: {fileName} ({ex.Message})";
            _log.Warning(msg);
            return new UninstallRunResult(null, false, stopwatch.Elapsed, msg);
        }

        _log.Info($"Poisto-ohjelma päättyi, koodi {exitCode}");

        var keyRemoved = await WaitForKeyRemovalAsync(program.KeyPath, ct).ConfigureAwait(false);
        _log.Info($"Uninstall-avain poistui: {(keyRemoved ? "kyllä" : "ei")}");

        return new UninstallRunResult(exitCode, keyRemoved, stopwatch.Elapsed, null);
    }

    private async Task<bool> WaitForKeyRemovalAsync(string keyPath, CancellationToken ct)
    {
        var deadline = Stopwatch.StartNew();
        while (true)
        {
            if (!RegistryPaths.KeyExists(keyPath)) return true;
            if (deadline.Elapsed >= _keyRemovalTimeout) return false;

            var remaining = _keyRemovalTimeout - deadline.Elapsed;
            await Task.Delay(remaining < _pollInterval ? remaining : _pollInterval, ct).ConfigureAwait(false);
        }
    }

    private static bool IsGuidKey(string keyName) =>
        keyName.Length == 38 && GuidPattern.IsMatch(keyName) && keyName[0] == '{';
}
