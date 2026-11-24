# RelicTracker

Track relic weapons, tools, and armor across every job - what you've finished, what step you're on, and what to farm next.

Covers Zodiac through Phantom / Cosmic tools, plus Eureka, Bozja, and Occult Crescent armor.

## What it does

RelicTracker tracks relic progress per character and shows which materials you still need. Optional helper plugins handle gathering, crafting, and buying.

## Install

1. `/xlsettings` → **Experimental** → Custom Plugin Repositories
2. Paste, tick, **Save**:

   ```text
   https://puni.sh/api/repository/kage
   ```

3. `/xlplugins` → **RelicTracker** → **Install**
4. Open with `/relictracker` or `/rtracker`

5. `/xlplugins` -> **MogVault** -> **Install** and enable it

MogVault is required for live inventory tracking and the Materials page. It detects relics, replicas, and materials across bags, retainers, dresser, and armoire.

## Using RelicTracker

- **All Relics** - pick a series to see its next unfinished upgrade in the same window. Weapons and tools default to your current job when supported. Pick any upgrade from the compact tier row to see its ordered plan, and open **Quest details** for the full walkthrough. Armor groups missing pieces by role or by job for Eureka job armor, with costs and any exchange options.
- **Materials** - see what to buy, craft, gather, or earn from quests and duties. One-time unlocks have their own tab, and armor costs are grouped by tier. Opening a relic series selects it in Materials. Choose **All relics in expansion** for the wider list.
- **Setup** - use the gear in the window title bar to check MogVault, optional helpers, and FFXIV Collect.

Right-click a relic or material in your inventory to jump straight to it.

## Optional helpers

- **FFXIV Collect** - recover finished relic progress for items you sold or desynthed. Link a public profile by entering its character ID; RelicTracker then fetches that profile's relic lists.
- **Artisan** (4.0.5.24+) - crafts supported turn-ins from an upgrade page.
- **Knightshopper** (1.0.2.0+) - use **Save shopping list** in Materials to review eligible purchases in Knightshopper. **Buy now** starts buying immediately where available.
- **Gather & Chill** (1.0.0.6+) - **Gather now** starts gathering the amount you're short. Your saved gather list stays intact and **Stop gathering** cancels the job you started from RelicTracker. **Add to Gather & Chill** saves materials for later.

## Questions

**Materials page unavailable?** Install and enable MogVault from `/xlplugins`, then wait for it to finish loading.

**Finished relic not marked?** Use **Recheck** to refresh your progress. For relics you no longer own, link [FFXIV Collect](https://ffxivcollect.com) in Setup.

**Craft with Artisan missing?** Update Artisan and check that the selected step has a supported crafting list.

**Helper action missing?** Open Setup to check that the helper is installed, enabled, and ready. In Knightshopper's **IPC** settings, turn on both purchase permissions and add RelicTracker to the allowed plugins.

## Support

[Issues](https://github.com/Kagekazu/RelicTracker/issues) · [Ko-fi](https://ko-fi.com/kagekazu)
