# RelicTracker

Dalamud plugin for tracking FFXIV relic materials across inventory and retainers, based on [Wyn's Relic Tracker](https://docs.google.com/spreadsheets/d/10E_We2y1fHTghcugem5TkSlpxcRQU-hmYh6LAcXlfmQ/edit) data.

## Features (v0.1)

- Expansion tabs: ARR, HW, SB, ShB, EW, DT, DoH/DoL
- Live owned counts via **Allagan Tools** IPC
- Materials reference sheet (where to farm)
- Per-weapon/tool quantities from extracted spreadsheet data

## Getting started

1. Install the .NET 10 SDK and a local Dalamud dev environment (XIVLauncher with dev plugin loading enabled).
2. Open **`RelicTracker.slnx`** at the repo root in Rider, then build **Debug | x64**.
3. Point Dev Plugin Locations at `RelicTracker/bin/x64/Debug/RelicTracker.dll`.

Unload the plugin or close FFXIV before rebuilding if Dalamud has the DLL locked.

## Dependencies

- [Allagan Tools](https://github.com/Critical-Impact/InventoryTools) (required for inventory counts)

## Commands

- `/relictracker` — open the tracker window

## Data updates

When Wyn releases a new tracker version, replace `data/Wyn's Relic Tracker.xlsx` and run:

```bash
python data/extract_wyn.py
```

Then rebuild the plugin.
