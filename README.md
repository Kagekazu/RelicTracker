# RelicTracker

Track relic weapons, tools, and armor across every job - what you've finished, what step you're on, and what to farm next.

Covers Zodiac through Phantom / Cosmic tools, plus Eureka, Bozja, and Occult Crescent armor.

## What it does

RelicTracker keeps per-character relic progress, shows the materials still needed, and can hand specific work to optional helper plugins. It does not gather, craft, or buy materials by itself.

## Install

1. `/xlsettings` → **Experimental** → Custom Plugin Repositories
2. Paste, tick, **Save**:

   ```text
   https://puni.sh/api/repository/kage
   ```

3. `/xlplugins` → **RelicTracker** → **Install**
4. Open with `/relictracker` or `/rtracker`

5. `/xlplugins` -> **MogVault** -> **Install** and enable it

MogVault is required. It detects relics and materials across bags, retainers, dresser, and armoire, including replicas.

## Tabs

- **Overview** - every relic line at a glance
- **Relic** - one job, step by step, with materials you're short
- **Tracker** - shopping list for unfinished jobs, grouped by farm location
- **Settings** - MogVault, optional helpers, and FFXIV Collect

Right-click a relic or material in inventory → jump straight to it. Progress is per character.

## Optional helpers

- **FFXIV Collect** - recover finished relic progress for items you sold or desynthed. Link a public profile by entering its character ID; RelicTracker then fetches that profile's relic lists.
- **Artisan** - crafts supported relic-tool turn-ins from the Relic tab.
- **Knightshopper** - buys missing Tracker materials after you allow RelicTracker in its IPC settings.
- **Gather & Chill** - adds missing gatherables to its saved gather list when installed.

Manual completion ticks are saved per character and stay marked until you uncheck them.

## Questions

**RelicTracker is blocked?** Install and enable MogVault from `/xlplugins`, then wait for it to finish loading. RelicTracker resumes automatically.

**Finished relic not marked?** Hit **Recheck**, link [FFXIV Collect](https://ffxivcollect.com) for relics you no longer own, or tick the step manually. Manual ticks stay marked until you clear them.

**Craft with Artisan missing?** Update Artisan; button only shows on crafter collectable steps.

**Helper action missing?** Open Settings to check that the helper is installed, enabled, and ready. Knightshopper also needs both IPC permissions enabled and RelicTracker added to its allowed plugins.

## Support

[Issues](https://github.com/Kagekazu/RelicTracker/issues) · [Ko-fi](https://ko-fi.com/kagekazu)
