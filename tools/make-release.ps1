<#
.SYNOPSIS
  Tekee PC Cleanerin julkaisupaketin publish\PcCleaner-<versio>-win-x64.zip ja tulostaa sen SHA-256-tiivisteen.

  Paketti on itsenäinen (.NET-ajonaika ja Windows App SDK mukana). Zipin juuressa on vain käynnistin
  "Start PC Cleaner.cmd", README.txt ja kansio PcCleaner, jossa ovat kaikki ohjelman tiedostot. Näin
  käyttäjä löytää käynnistettävän tiedoston satojen ajonaikaisten tiedostojen joukosta.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\make-release.ps1 -Version 1.0.1
#>
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version
)
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$publish = Join-Path $root 'publish'
$stage = Join-Path $publish 'release'
$app = Join-Path $stage 'PcCleaner'
$zip = Join-Path $publish "PcCleaner-$Version-win-x64.zip"

if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
if (Test-Path $zip) { Remove-Item -Force $zip }

dotnet publish (Join-Path $root 'src\Cleaner.UI') -c Release -o $app --self-contained true -p:Version=$Version
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
if (-not (Test-Path (Join-Path $app 'PcCleaner.exe'))) { throw 'PcCleaner.exe is missing from the publish output' }

# Käynnistin: %~dp0 on cmd-tiedoston oma kansio, joten paketin voi purkaa minne tahansa.
# start palaa heti, ja konsoli-ikkuna sulkeutuu ennen UAC-kehotetta.
$launcher = "@echo off`r`nstart `"`" `"%~dp0PcCleaner\PcCleaner.exe`"`r`n"
[IO.File]::WriteAllText((Join-Path $stage 'Start PC Cleaner.cmd'), $launcher, [Text.Encoding]::ASCII)

$readme = @"
PC Cleaner $Version
https://github.com/Vilippo/PcCleaner

Start: double-click "Start PC Cleaner.cmd", or run PcCleaner\PcCleaner.exe.
Keep the PcCleaner folder together: the program needs all of its files.

- Windows asks for administrator rights (UAC) on every start.
- The exe is not code-signed. If SmartScreen shows "Windows protected your PC",
  choose "More info" and then "Run anyway".
- Nothing else needs to be installed. Requires 64-bit Windows 11 or
  Windows 10 version 2004 or newer.
- Settings, logs, quarantine and registry backups are in %LOCALAPPDATA%\PcCleaner.
  To uninstall, delete this folder and, if you like, %LOCALAPPDATA%\PcCleaner.

---

Käynnistys: kaksoisnapsauta "Start PC Cleaner.cmd" tai käynnistä PcCleaner\PcCleaner.exe.
Pidä PcCleaner-kansio kokonaisena: ohjelma tarvitsee kaikki sen tiedostot.

- Windows kysyy järjestelmänvalvojan oikeudet (UAC) joka käynnistyksessä.
- Exe ei ole allekirjoitettu. Jos SmartScreen näyttää ilmoituksen "Windows suojasi
  tietokonettasi", valitse "Lisätietoja" ja sitten "Suorita silti".
- Muuta ei tarvitse asentaa. Vaatii 64-bittisen Windows 11:n tai Windows 10:n
  version 2004 tai uudemman.
- Asetukset, lokit, karanteeni ja rekisterin varmuuskopiot ovat kansiossa
  %LOCALAPPDATA%\PcCleaner. Poista ohjelma poistamalla tämä kansio ja halutessasi
  myös %LOCALAPPDATA%\PcCleaner.
"@
# UTF-8 BOM:lla, jotta Muistio näyttää ääkköset oikein myös vanhemmissa Windows-versioissa.
[IO.File]::WriteAllText((Join-Path $stage 'README.txt'), ($readme -replace "`r?`n", "`r`n"), (New-Object Text.UTF8Encoding $true))

Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal

$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
$size = (Get-Item $zip).Length / 1MB
Write-Host ("{0}  {1:N1} MB" -f $zip, $size)
Write-Host "SHA-256: $hash"
