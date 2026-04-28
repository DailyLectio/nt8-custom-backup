# NT8 Backup Plan

## Current State

- The live NT8 folder already has a Git repository initialized.
- The repo is connected to `https://github.com/DailyLectio/nt8-custom-backup.git`.
- `main` is tracking `origin/main`.
- A Windows scheduled task named `NT8 GitHub Auto Backup` is registered to run
  every 15 minutes.
- The most important backup scope is code plus templates, not the entire
  NinjaTrader platform folder.

## Organization Rule

Do not physically reorganize or move live NinjaTrader files unless NT8 itself
expects that location. NT8 uses specific folders for NinjaScript and templates,
so the backup should organize by Git scope, naming, documentation, and tags while
leaving the working files where NT8 can still find them.

## Backed Up To GitHub

Primary source:

- `bin/Custom/Indicators/**/*.cs`
- `bin/Custom/Indicators/**/*.xaml`
- `bin/Custom/Indicators/**/*.resx`
- `bin/Custom/Strategies/**/*.cs`
- `bin/Custom/**/*.cs`
- `bin/Custom/**/*.xaml`
- `bin/Custom/**/*.resx`
- `bin/Custom/*.csproj`
- `bin/Custom/NinjaTrader.Custom.xml`

Confirmed NT8 custom folders:

- `C:\Users\Valued Customer\Documents\NinjaTrader 8\bin\Custom\Indicators`
- `C:\Users\Valued Customer\Documents\NinjaTrader 8\bin\Custom\Strategies`

As of the latest audit, Git is tracking 197 files under `bin/Custom/Indicators`
and 98 files under `bin/Custom/Strategies`.

Templates and strategy settings:

- `templates/**/*.xml`
- `templates/**/*.cs`
- `RECOVERED_TEMPLATES/**/*.xml`
- `Strategy/**/*.xml`
- `Strategy/**/*.cs`
- `Strategy/**/*.txt`

Loose helper/source files:

- root `*.cs`
- `HMM_Watchdog.py`

## Not Backed Up To GitHub

These are excluded because they are generated, noisy, large, or potentially
sensitive:

- `Config.xml`
- `UI.xml`
- `db/`
- `cache/`
- `tmp/`
- `log/`
- `trace/`
- `workspaces/`
- `strategyanalyzerlogs/`
- `StrategyLogs/`
- `EBWebView/`
- `import/`, `incoming/`, `outgoing/`, `export/`
- live data files such as `Live_*_Data.txt`, `Session_Snapshot.csv`, and
  `TB_Tag.csv`
- binaries such as `.dll`, `.pdb`, `.zip`, `.bak`

If workspace layouts or platform configuration need backup later, store them in
a separate private/encrypted local snapshot rather than pushing them to GitHub by
default.

## Priority Model Groups

V3C strategy code:

- `bin/Custom/Strategies/V3_Compression_Sniper_V3C.cs`
- `bin/Custom/Strategies/V3_Expansion_Rider_V3C.cs`
- `bin/Custom/Strategies/V3_Value_Fader_V3C.cs`
- `bin/Custom/Strategies/MomoV3C.cs`
- `bin/Custom/Strategies/MomoV3CB.cs`
- `bin/Custom/Strategies/PineV3C.cs`
- `bin/Custom/Strategies/ADXXV3C.cs`
- `bin/Custom/Strategies/ADXDIV3C.cs`

V3D strategy code:

- `bin/Custom/Strategies/MomentumV3D.cs`
- `bin/Custom/Strategies/MomentumV3DB.cs`
- `bin/Custom/Strategies/ExpansionV3D.cs`
- `bin/Custom/Strategies/ExpansionV3DB.cs`
- `bin/Custom/Strategies/FaderV3D.cs`
- `bin/Custom/Strategies/FaderV3DB.cs`
- `bin/Custom/Strategies/SniperV3D.cs`
- `bin/Custom/Strategies/SniperV3DB.cs`
- `bin/Custom/Strategies/ADXDIV3D.cs`
- `bin/Custom/Strategies/ADXDIV3DB.cs`
- `bin/Custom/Strategies/ADXDIV3DC.cs`

V3C/V3D support indicators:

- `bin/Custom/Indicators/RegimeMatrixHUDV3C.cs`
- `bin/Custom/Indicators/RegimeMatrixHUDV3D.cs`
- `bin/Custom/Indicators/TradeLogExporterV3D.cs`
- `bin/Custom/Indicators/TradeLogExporter_V3D.cs`

Matching templates are under `templates/Strategy/`, especially folders with
`V3C`, `V3D`, `V3_`, `Momo`, `Pine`, `ADXX`, `ADX_DI`, `Expansion`, `Fader`,
`Momentum`, and `Sniper` in their names.

## GitHub Setup

Use a private GitHub repository. A good repo name would be:

`nt8-custom-backup`

After the private repo exists, connect this local folder to it:

```powershell
git remote add origin https://github.com/<your-user-or-org>/nt8-custom-backup.git
git branch -M main
git add -A
git commit -m "Initial NT8 strategy and template backup"
git push -u origin main
```

If Git asks for identity:

```powershell
git config user.name "Your Name"
git config user.email "you@example.com"
```

## Auto Backup

The safer auto-save pattern is scheduled polling. Every 15 minutes is usually
enough: it commits only when files changed, then pushes to GitHub if a remote is
configured. This avoids fragile "commit on every single file write" behavior
while NT8 is compiling or saving templates.

Manual test:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\nt8-autobackup.ps1
```

Register scheduled task after the manual test works:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\register-nt8-autobackup-task.ps1 -IntervalMinutes 15
```

The backup log is written outside the repo at:

`%LOCALAPPDATA%\NT8GitBackup\nt8-autobackup.log`

If Windows blocks that location, the script falls back to `.nt8-backup-logs/`
inside this folder. That fallback folder is ignored by Git.

## Restore Notes

For a restore, close NinjaTrader first. Then either clone the GitHub repo back to
`Documents\NinjaTrader 8` on a replacement machine or copy the backed-up folders
over an existing NT8 install. After restoring, open NT8 and compile NinjaScript
from the NinjaScript Editor.

Before major changes, make a named checkpoint:

```powershell
git tag checkpoint-before-<change-name>
git push origin checkpoint-before-<change-name>
```
