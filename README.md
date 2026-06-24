# RelicTracker

Dalamud plugin for tracking FFXIV relic materials across inventory and retainers, based on [Wyn's Relic Tracker](https://docs.google.com/spreadsheets/d/10E_We2y1fHTghcugem5TkSlpxcRQU-hmYh6LAcXlfmQ/edit) data.

## Features

- **Overview** — at-a-glance status of every relic line (ARR through Dawntrail weapons + DoH/DoL tools): how many jobs are finished and what step the rest are on, synced from **FFXIV Collect**.
- **Relic** — pick an expansion, relic line and job to get a step-by-step checklist: tick off each upgrade, see the current step highlighted, and view the items needed (with live owned counts) for what's next. Inspired by [ffxivrelictracker.com](https://ffxivrelictracker.com/).
- **Tracker** — per-expansion materials you still need, with live owned counts via **Allagan Tools** IPC.
- Materials reference sheet (where to farm).

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

The plugin reads two data sources, both bundled into `Data/` at build time:

- **Materials** come from Wyn's spreadsheet. When Wyn ships a new version, replace `data/Wyn's Relic Tracker.xlsx` and run `python data/extract_wyn.py`.
- **Relic lines** (the Overview's steps and job counts) come from the FFXIV Collect relic index. When a new relic releases, refresh it with `python data/extract_relics.py` (delete `data/extracted/_relics_index_raw.json` first to force a fresh pull).
- **Step notes** (the "to do now" guidance on the Relic tab) are hand-curated in `data/extracted/relic_step_notes.json`, sourced from the [Console Games Wiki](https://ffxiv.consolegameswiki.com/wiki/Relic_Weapons). Edit that file directly to add or refine guidance, then rebuild.

Then rebuild the plugin.
