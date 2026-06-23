# RelicTracker

Dalamud plugin for tracking FFXIV relic materials across inventory and retainers, based on [Wyn's Relic Tracker](https://docs.google.com/spreadsheets/d/10E_We2y1fHTghcugem5TkSlpxcRQU-hmYh6LAcXlfmQ/edit) data.

## Features (v0.1)

- Expansion tabs: ARR, HW, SB, ShB, EW, DT, DoH/DoL
- Live owned counts via **Allagan Tools** IPC
- Materials reference sheet (where to farm)
- Per-weapon/tool quantities from extracted spreadsheet data

## Dependencies

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Allagan Tools](https://github.com/Critical-Impact/InventoryTools) (required for inventory counts)
- XIVLauncher with Dalamud dev plugin loading

## Build (IDE)

**Important:** open **`RelicTracker.slnx`** at the repo root — not just the folder.  
If Rider/Cursor opened the directory only, you will not get a .NET Build menu.

| IDE | How to build |
|-----|----------------|
| **Rider** | File → Open → `RelicTracker.slnx`, then **Build → Build Solution** (Debug \| x64), or run **Build Debug** from the run configurations dropdown |
| **Cursor / VS Code** | Terminal → **Run Build Task** (`Ctrl+Shift+B`) — uses `.vscode/tasks.json` |
| **Terminal** | `dotnet build RelicTracker.slnx -c Debug -p:Platform=x64` |

Dev plugin DLL: `RelicTracker/bin/x64/Debug/RelicTracker.dll`

## Commands

- `/relictracker` — open the tracker window

## Data updates

When Wyn releases a new tracker version, replace `data/Wyn's Relic Tracker.xlsx` and run:

```bash
python data/extract_wyn.py
```

Then rebuild the plugin.

## Roadmap

- Job/step progress checkboxes (Wyn-style)
- FFXIV Collect read-only owned relics/tools
- Import Wyn progress strings
