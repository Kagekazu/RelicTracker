# RelicTracker

A Dalamud plugin that keeps track of your relic weapons, relic tools, and relic armor across every job.
It shows what you've finished, what step each job is on, and exactly what you still need to farm.

Covers every relic line from A Realm Reborn's Zodiac weapons to Dawntrail's Phantom weapons and
Cosmic tools, plus the Eureka, Bozja, and Occult Crescent armor sets.

## Install

1. In game, type `/xlsettings` and open the **Experimental** tab.
2. Under **Custom Plugin Repositories**, paste this URL, tick the box, and click **Save**:

   ```text
   https://puni.sh/api/repository/kage
   ```

3. Type `/xlplugins`, search for **RelicTracker**, and click **Install**.
4. Open it with `/relictracker` (or the shorter `/rtracker`).

We also recommend installing **Allagan Tools**. RelicTracker works without it, but with it your
progress and material counts fill in automatically. See [Works best with](#works-best-with).

## The tabs

### Overview: the big picture

Every relic line on one screen: how many jobs have maxed it, a progress bar, and where your
unfinished jobs are (for example, "3 on Anemos, 2 on Pagos"). Hover a line to see how many jobs
reached each step.

Tick **Hide finished lines** to see only what's left.

### Relic: one job, step by step

Pick an expansion, a relic line, and a job. You'll get:

- a checklist of every step, with your **current step** highlighted
- notes on what to do next, tailored to crafters, gatherers, or fishers on tool lines
- the materials for your current step, with how many you own and how many you're short
- an **All jobs** grid, showing every job's progress on that line at a glance

For armor lines (Eurekan, Resistance, Phantom), you'll see each set's pieces and what they cost.
Hover a piece to see its price.

### Tracker: your farming list

Pick an expansion (and optionally one relic line) to get a single shopping list for **every job
you haven't finished**. Materials are grouped by where you farm them, so you can see at a glance
how many Protean Crystals you need or which Bozja memories you're still missing.

- **Hide finished materials** keeps the list down to what you still need.
- The list gets shorter as you finish jobs or tick steps off.
- Armor currency counts the pieces you already own, so you're never told to farm for gear you have.
- On the Zodiac line, prefarmed quest rewards in your inventory count toward their materials.

### Settings

Shows whether Allagan Tools and Artisan are connected, plus the FFXIV Collect link and a
**Hide Physeos** option (see the [Questions](#questions) section).

## Handy extras

- **Right-click to jump there.** Right-click a relic, replica, relic material, or armor piece in
  your inventory and choose **Open Relic** or **Open Tracker** to go straight to it.
- **Tick things off yourself.** Sold or desynthed a relic? Tick its steps (or armor pieces) on the
  **Relic** tab and they'll count as done.
- **Per character.** Progress is saved separately for each of your characters.

## Works best with

| Plugin | What it adds | Needed? |
|---|---|---|
| **Allagan Tools** | Counts items across your bags, retainers, glamour dresser, and armoire. Spots relics (and replicas) you own and marks those steps done automatically. | Recommended |
| **Artisan** | Adds a **Craft with Artisan** button to crafter relic tool steps, which starts the right crafting list for you. | Optional |
| **FFXIV Collect** (website) | Remembers relics you've finished but no longer have, e.g. sold or desynthed. | Optional |

To link FFXIV Collect, open your profile on [ffxivcollect.com](https://ffxivcollect.com). The
number at the end of the address (`ffxivcollect.com/characters/123456`) is your character ID.
Paste it into **Settings → FFXIV Collect** and click **Save ID**.

## Questions

**My owned counts are all 0.**
Install and enable Allagan Tools, then check **Settings**: it should say *Connected*. Without
it, RelicTracker can only see what's in your bags right now.

**A finished relic isn't showing as done.**
Click **Recheck** on the Overview or Relic tab. If you no longer have the item, either link
FFXIV Collect or tick the steps yourself on the **Relic** tab.

**Do I need Physeos?**
Physeos is an optional final Eureka upgrade from the Baldesion Arsenal. It looks the same as the
Eureka weapon outside Eureka and doesn't count as a new relic for achievements. Turn on
**Settings → Hide Physeos** to treat Eureka as the end of the line.

**The Craft with Artisan button is missing.**
Make sure Artisan is installed and up to date. The button only shows on crafter steps that need
crafted collectables. Buy the scrip materials first; Artisan won't buy them for you.

## Support

Found a bug or have an idea? [Open an issue](https://github.com/Kagekazu/RelicTracker/issues).
If RelicTracker saved you some spreadsheet time, you can buy me a coffee on
[Ko-fi](https://ko-fi.com/kagekazu) (the heart icon in the plugin's title bar).
