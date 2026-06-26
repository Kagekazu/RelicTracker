# RelicTracker

Track your FFXIV relic weapons, tools, and armor across every job — see what's
finished, what you're working on, and exactly what materials you still need to
farm, with live inventory counts pulled from Allagan Tools.

## What it does

- **Overview** — every relic line from A Realm Reborn through Dawntrail, plus the
  Disciple of the Hand & Land tools, with how many jobs are finished and what step
  the rest are on.
- **Relic** — pick an expansion, line, and job for a step-by-step checklist. Tick
  off each upgrade, see the step you're on highlighted, and check the items the
  next step needs with live owned counts.
- **Tracker** — everything you still need for an expansion in one shopping list,
  grouped by where you farm it, plus field-operation armor currency costs. Owned
  counts come straight from your inventory and retainers.

## How it works

RelicTracker reads your inventory through **Allagan Tools** to show how many of
each material you already have. Install and enable Allagan Tools for owned counts
to appear.

Progress can be tracked two ways:

- **Manual** — tick steps and armor pieces yourself on the Relic tab. The Overview
  and Tracker update to match.
- **FFXIV Collect (optional)** — add your [FFXIV Collect](https://ffxivcollect.com/)
  character ID on the Settings tab and finished relics fill in automatically. Your
  ID is the number in your profile URL (`ffxivcollect.com/characters/XXXXXXXX`).

The Tracker's "still needed" numbers cover every job that hasn't finished a step
yet, so as you complete relics (or tick them off) the list shrinks to just what's
left.

## Getting started

1. Install and enable **Allagan Tools** for inventory counts.
2. Open RelicTracker with `/relictracker` (or `/rtracker`).
3. (Optional) Add your FFXIV Collect ID on the **Settings** tab to auto-fill
   finished relics. Otherwise, tick steps yourself on the **Relic** tab.

## Commands

- `/relictracker` — open the window
- `/rtracker` — short alias
