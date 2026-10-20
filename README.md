# PC Cleaner

**English** | [Suomi](README.fi.md)

A modular cleanup tool for Windows 11. C# / .NET 8. WinUI 3 desktop app and a command-line interface.
PC Cleaner is a public open source project (https://github.com/Vilippo/PcCleaner) under the MIT license.
The program runs in English and Finnish.

## Download and install

1. Download `PcCleaner-<version>-win-x64.zip` from the [latest release](https://github.com/Vilippo/PcCleaner/releases/latest).
2. Extract the whole zip to a folder of your choice, for example `C:\Tools`. The zip contains `Start PC Cleaner.cmd`,
   `README.txt` and the folder `PcCleaner` with all program files. Keep that folder together.
3. Double-click `Start PC Cleaner.cmd`, or run `PcCleaner\PcCleaner.exe` directly.

Requirements: 64-bit Windows 11 or Windows 10 version 2004 or newer. Nothing else needs to be installed: the .NET
runtime and the Windows App SDK are included in the package.

Things you will see on the first start:

- Windows asks for administrator rights (UAC) every time. The app needs them for system folders and HKLM registry keys.
- The exe is not code-signed, so Microsoft Defender SmartScreen may show "Windows protected your PC". Choose
  "More info" and then "Run anyway". If you want to check the download first, the release notes list the SHA-256 hash
  of the zip (`Get-FileHash .\PcCleaner-<version>-win-x64.zip`).

Nothing is deleted before you confirm, and every cleanup starts with a dry run. See [Safety principles](#safety-principles).
The AI assistant is optional and needs your own Anthropic API key, see [AI assistant](#ai-assistant).

The app keeps its data in `%LOCALAPPDATA%\PcCleaner`: `settings.json`, `logs`, `quarantine` and `registry-backups`.
To uninstall, delete the program folder. Delete `%LOCALAPPDATA%\PcCleaner` too if you do not need the logs or backups.

The release contains only the desktop app. To use the command line, build it from source (see below).

## Structure

```
PcCleaner.sln
├── src/
│   ├── Cleaner.Core/               Core: ICleanerModule, models, rules (JSON), CleanerEngine, logging.
│   │   ├── Localization/           Loc, LocalizedText: English and Finnish texts
│   │   └── Services/               PathGuard, SafeFileDeleter, PathExpander, ProcessChecker, ElevationHelper,
│   │                               Quarantine, BackupSessions, AppSettings, Registry/ (RegistryBackup, RegistryGuard, RegistryPaths)
│   ├── Cleaner.Modules.Files/      File cleanup + browser caches (rules/files.json, rules/browsers.json) + Recycle Bin
│   ├── Cleaner.Modules.Startup/    Startup programs: Run keys, startup folders, Task Scheduler, Store StartupTask
│   ├── Cleaner.Modules.DiskUsage/  Disk usage (read-only) + treemap algorithm
│   ├── Cleaner.Modules.Registry/   Registry cleanup: orphaned Uninstall, file extension, ProgId, MUICache and MRU entries
│   ├── Cleaner.Modules.Uninstall/  Uninstall programs: runs the uninstaller, finds leftovers, quarantine
│   ├── Cleaner.Modules.Processes/  Processes: classifies and ends background processes in the user session (rules/processes/known.json)
│   ├── Cleaner.Ai/                 AI assistant: Claude API (NuGet Anthropic), scan digests, path sanitizer, verdicts
│   ├── Cleaner.Cli/                Command line: scan, clean, modules, uninstall, restore (ModuleFactory)
│   └── Cleaner.UI/                 WinUI 3 app PcCleaner.exe: MainWindow, Views/ (MainView), ViewModels/,
│       │                           Services/ (UiModuleFactory), Theming/ (Appearance: palettes and fonts)
│       └── Assets/Fonts/           InterVariable.ttf and LICENSE-Inter.txt
├── tests/
│   └── Cleaner.Tests/              xUnit tests (modules, services, quarantine, treemap, AI)
├── tools/
│   ├── make-icon.ps1               Generates the palette icons PcCleaner-<palette>.ico and PcCleaner.ico/.png (mint)
│   ├── make-release.ps1            Builds the self-contained release zip publish\PcCleaner-<version>-win-x64.zip
│   └── snap-window.py              Takes a screenshot of the app window with PrintWindow
└── docs/
    └── ARCHITECTURE.md             Technical specification (in Finnish)
```

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022 (17.8+) or VS Code + C# Dev Kit

## Build and run

Desktop app (WinUI 3, Visual Studio not required):

```powershell
dotnet build
dotnet test
dotnet run --project src\Cleaner.UI
```

The exe is in `src\Cleaner.UI\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\PcCleaner.exe`.
The Windows App SDK runtime is copied next to the program, so no separate install is needed.
The desktop app always asks for administrator rights (UAC) so that it can handle system folders and HKLM keys.
The command line runs as a normal user.

Publish folder (Release, self-contained, can be copied to another computer without installing .NET):

```powershell
dotnet publish src\Cleaner.UI -c Release -o publish\PcCleaner --self-contained true
```

Release zip (`publish\PcCleaner-<version>-win-x64.zip` with the launcher, README.txt and the `PcCleaner` folder;
prints the SHA-256 hash):

```powershell
powershell -ExecutionPolicy Bypass -File tools\make-release.ps1 -Version 1.0.1
```

On the Disk usage page you can limit the scan to one folder, choose the top N and browse a treemap (a click opens the
subfolder). The app icon is generated by the script `tools\make-icon.ps1` (no image editor needed).
Cleaning asks twice: first a dry run summary, then a confirmation. High-risk items are in their own section, collapsed
by default, and they are never selected automatically.

Command line:

```powershell
cd src\Cleaner.Cli
dotnet run -- modules
dotnet run -- scan
dotnet run -- clean              # dry run, deletes nothing
dotnet run -- clean --apply      # deletes, asks for confirmation
dotnet run -- scan --include-disabled   # also rules that are off by default (Prefetch, Windows.old)
dotnet run -- scan --module startup     # startup programs (runs only when requested)
dotnet run -- clean --module startup --item "startup:hkcu-run:Spotify" --apply   # disable one
dotnet run -- scan --module diskusage --path C:\Users\%USERNAME% --top 10         # largest folders and files
dotnet run -- scan --module registry                                             # orphaned registry entries (read-only)
dotnet run -- clean --module registry --item "registry:mru:" --apply              # clear MRU lists, backup first
dotnet run -- scan --module processes                                            # background processes in the user session, classified
dotnet run -- clean --module processes --item "processes:<pid>:<start-time>" --apply   # end one process, asks for confirmation
dotnet run -- uninstall                                                           # list installed programs by name
dotnet run -- uninstall --sort date                                               # same, newest install date first
dotnet run -- uninstall --program "7-Zip"                                         # run the uninstaller, list leftovers as a dry run
dotnet run -- uninstall --program "7-Zip" --quiet                                 # same with the quiet uninstall command
dotnet run -- uninstall --program "7-Zip" --leftovers-only                        # only search for leftovers, no uninstaller
dotnet run -- uninstall --program "7-Zip" --leftovers-only --apply                # move preselected (*) items to quarantine
dotnet run -- uninstall --program "7-Zip" --leftovers-only --apply --all          # all leftovers, including High risk
dotnet run -- restore                                                             # list backups (registry + quarantine)
dotnet run -- restore 20261019-170000                                             # restore a session (reg import + quarantine)
dotnet run -- --lang fi scan                                                      # Finnish output, see Language
```

Uninstall programs (`uninstall`) runs the program's own uninstaller, waits for it to finish and searches for leftovers:
folders, shortcuts, registry keys and values. `--program` accepts the exact name or an unambiguous part of the name.
With several matches the command lists the candidates and returns exit code 2. Leftovers are printed as a dry run, and
preselected items are marked with `*`. `--apply` moves the preselected items, the items limited by `--item`, or all
items with `--all`, and asks for confirmation `[y/N]` (in Finnish `[k/E]`) unless `--yes` is given. Folders and files
move to `%LOCALAPPDATA%\PcCleaner\quarantine\<timestamp>\`, and registry data is exported to `.reg` files with the
same timestamp. The output shows the quarantine folder and the restore command.
Win32 programs only. Store apps do not appear in the list.

In the desktop app the "Uninstall programs" page has a search box, a program list (name, version, publisher, size) and
the buttons "Refresh list", "Uninstall program" and "Find leftovers". "Uninstall program" shows a waiting state
"Waiting for uninstaller: <name>" (Cancel stops the waiting, not the uninstall) and then scans for leftovers
automatically. Leftovers appear in the normal category view, and Clean moves the selected items to quarantine.
"Restore backup..." lists both registry backups and quarantine sessions.

Registry cleanup (`registry`) has a limited scope: orphaned Uninstall entries, file type associations, MUICache and
most recently used (MRU) lists. Every key that will change is exported to a `.reg` file in
`%LOCALAPPDATA%\PcCleaner\registry-backups\<timestamp>\` before the change. If the export fails, the item is not
touched. CLSID, Interface, TypeLib, Services, Drivers, SharedDLLs and Fonts keys are never touched.

Disk usage (`diskusage`) only lists the largest folders and files. `--apply` deletes only the files limited by
`--item`, never folders. Scanning a whole drive takes minutes, so limit it with `--path`.

Processes (`processes`) lists processes in the user session whose exe is not in the Windows folder, in six categories
(open programs, missing program file, orphaned helper processes, known, idle and active background processes).
The scan measures CPU usage for 3 s. Ending a process checks the start time in the item id, so a recycled PID is never
ended, and every ended process is logged with its name, PID and path. PC Cleaner's own process and all its ancestors
(for example the shell and Windows Terminal the command was run from) are protected: they are never listed or ended.
Warning: the CLI `clean` command does not filter by preselection or risk. `--apply` without an exact `--item` id (or
with only the prefix `"processes:"`) ends all listed processes, including open programs.

Startup programs are not deleted. They are disabled the same way Task Manager does it (the `StartupApproved` key, or
the StartupTask state for Store apps). You can turn them back on from the Startup apps tab in Task Manager.

On the command line, system folders (`Windows\Temp`, `SoftwareDistribution`) require PowerShell to be started as
administrator. Without it they are skipped.

## Language

The default language is English. The Windows display language does not affect it.

- In the desktop app, the selector `English`/`Suomi` on the settings page changes the language immediately, without
  a restart, and saves the choice to `%LOCALAPPDATA%\PcCleaner\settings.json`. The selector is disabled while the app
  scans, cleans, restores or waits for an uninstaller.
- On the command line, `--lang en|fi` sets the language for one run and overrides the saved choice, for example
  `dotnet run -- --lang fi scan`. Without the flag the command line uses the language in `settings.json`, or English
  if the file does not exist. The command line never writes `settings.json`.

Results that were already scanned (category names, item descriptions) and log lines already written keep the language
they were created in. They switch to the new language on the next scan.

## Appearance

The gear icon in the navigation pane opens the settings page. It has a color palette, a font and the language.

- Palettes: Mint (default), Indigo, Steel, Graphite and Sun. Every palette has a light and a dark version, and the app
  follows the light or dark setting in Windows. In high contrast mode the app uses the Windows system colors.
- Fonts: Bahnschrift and Segoe UI Variable (default, Bahnschrift for headings and numbers), Segoe UI Variable, and
  Inter. Bahnschrift and Segoe UI Variable come with Windows 11. Inter is included with the app.
- A change applies to the whole window immediately, without a restart. The selectors are disabled while the app is busy.
- The title bar and taskbar icon follow the palette. The exe icon is always mint.

The choices are saved to `%LOCALAPPDATA%\PcCleaner\settings.json` in the fields `language`, `palette` and `font`:

```json
{
  "language": "fi",
  "palette": "indigo",
  "font": "inter"
}
```

Palette values are `mint`, `indigo`, `steel`, `graphite` and `sun`. Font values are `bahnschrift`, `segoe` and `inter`.
A missing or unknown value gives the default (`mint`, `bahnschrift`), and the file is not rewritten at startup.
Saving one setting keeps the other fields.

## AI assistant

The desktop app has an AI assistant that uses the Claude API from Anthropic. The button left of the log button in the
bottom bar opens it as a 400 px pane on the right, next to the module page. The assistant:

- Reviews the scan results of the selected module ("Review results"). Every category, and items that need a comment,
  get a verdict `safe`, `caution` or `keep` with a reason. The verdicts appear as badges on the category cards, and
  the tooltip shows the reason. A category verdict applies to all of its items.
- Assesses the condition of the computer ("PC health") from system facts and the current scan results.
- Answers your questions in the same conversation.

By default the assistant only gives advice and does not change the selection. "Deselect flagged" removes the selection
from every item marked `caution` or `keep`. The conversation is kept in memory only (at most 20 messages), and "New
conversation" clears it. Answers are written in the app language.

The assistant needs an Anthropic API key in the environment variable `ANTHROPIC_API_KEY`:

```powershell
setx ANTHROPIC_API_KEY sk-ant-...
```

You can also set it in Windows under System > About > Advanced system settings > Environment variables. Restart PC Cleaner
afterwards, because the app reads the variable at startup. Without a key the pane shows a notice and the buttons that
call the assistant are disabled; the Auto mode switch and "New conversation" still work, and the rest of the app works
as before. The key is never written to `settings.json`, the log or the pane.

API usage is billed to your Anthropic account according to Anthropic's pricing. Every review, health check and question
is one API call, and each call sends the whole conversation again.

What is sent to Anthropic:

- Review: the selected module's results as text. Per category the name, risk, item count, total size and number of
  selected items; per item the size, path, description and whether it is selected. A category with more than 40 items
  sends its 20 largest, and one review lists at most 400 items. The rest are only counted.
- PC health: Windows version, CPU cores, memory, uptime, whether the app runs as administrator, fixed drives (name,
  label, size, free space), and per module the item count, total size and five largest categories. No paths.
- Your questions as you typed them.
- In paths and descriptions your user profile path is replaced with `%USERPROFILE%`, and your user name in other
  `\Users\<name>\` paths with `%USERNAME%`.

Not sent: the log, `settings.json`, the API key, file contents or registry values read separately. Registry items send
the same path and description that the card shows.

Auto mode lets the assistant change the selection. The "Auto mode" switch is in the pane and on the settings page, and
it is off by default. In Auto mode a review selects items marked `safe` and deselects items marked `caution` or `keep`
(only in the default modules; in opt-in modules the review only gives advice).
The "Automatic cleanup" button then scans the default modules (File cleanup, Browser caches, Recycle Bin), reviews every
module with results, applies the selections, runs a dry run and shows one confirmation dialog with the assistant's
summaries, the dry run count and size, and the skipped items. Nothing is deleted before you confirm, and Cancel stops
any step before the dialog. Limits:

- High-risk items are never selected, even when the assistant marks them `safe`.
- Opt-in modules (Startup programs, Disk usage, Registry cleanup, Uninstall programs, Processes) are never included.
- Files do not go to the Recycle Bin. The normal Clean button keeps its two confirmations.

Settings in `settings.json`:

```json
{
  "aiModel": "claude-opus-5-5",
  "aiAutoMode": false
}
```

`aiModel` is the model id as is (default `claude-opus-5-5`). The app has no selector for it, so edit the file and restart.
`aiAutoMode` is saved by the switch. Missing fields give the defaults.

## Safety principles

1. **Dry run is the default.** Nothing is deleted without `--apply`.
2. **Age limit.** Temp files are deleted only after 24 h, so running programs are not affected.
3. **PathGuard.** Drive roots, the Windows, Program Files and user folders are never deleted, even if a rule file has an error.
4. **Browser open = skip.** A browser cache is not touched while the browser is running.
5. **Everything is logged** to `%LOCALAPPDATA%\PcCleaner\logs\`.
6. **The registry is always backed up first.** A key is not changed without a successful `.reg` export, and restoring takes one command.
7. **Leftovers go to quarantine, not deletion.** Uninstall programs moves leftover folders and files to quarantine, and `pccleaner restore <timestamp>` brings them back.

## Disclaimer

PC Cleaner deletes files, changes the registry and startup settings, and ends processes. It is provided as is,
without warranty of any kind. The author is not liable for any damage or data loss caused by using it. You use it at
your own risk. Back up your important data before you clean.

## Adding a new cleanup target

Add a rule to `src/Cleaner.Modules.Files/rules/files.json`. No code changes are needed.
Write `name` and `description` in English and the Finnish text in `nameFi` and `descriptionFi`. If the Finnish
fields are missing, the English text is shown in both languages.
The fields are described in `docs/ARCHITECTURE.md`.

## Adding a new module

1. Create the project `src/Cleaner.Modules.<Name>/` and reference the `Cleaner.Core` project.
2. Implement `ICleanerModule`. Write user-visible texts as `Loc.T("English", "Suomi")` pairs.
3. Register the module in `ModuleFactory.CreateAll` (CLI) and `UiModuleFactory.CreateAll` (WinUI).

## License

PC Cleaner is released under the MIT License. See [LICENSE](LICENSE). The software is provided "as is", without
warranty of any kind, and you use it at your own risk.

The Inter font (`src/Cleaner.UI/Assets/Fonts/InterVariable.ttf`) is licensed under the SIL Open Font License 1.1.
See `src/Cleaner.UI/Assets/Fonts/LICENSE-Inter.txt`.
