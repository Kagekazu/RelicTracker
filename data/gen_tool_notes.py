"""
Rewrites the five DoH/DoL tool-line entries in data/extracted/relic_step_notes.json with
discipline-tagged step notes. Text before the first [[tag]] is shared intro shown to every job;
[[Crafters]] / [[Gatherers]] / [[Fisher]] sections are shown only to the matching discipline
(slots 0-7 / 8-9 / 10) - see PluginUI.Detail.NoteForDiscipline. Weapon/armor notes in the same
file are left untouched. Re-run after editing; do not hand-edit these five entries in the JSON.
"""
import json, os

def N(*lines):
    """One note from its lines. Markup (drawn by PluginUI.DrawNote): "1. " numbered step, "- " bullet,
    "Tip: " tip, a short line ending in ':' is a sub-heading. [[Crafters]] / [[Gatherers]] / [[Fisher]]
    start a discipline section; text before the first tag is shown to every job."""
    return "\n".join(lines)

TOOL_NOTES = {
 "Lucis Tools": {
  "_line": N(
   "ARR Disciple of the Hand & Land relic tools. Start 'Just Tooling Around' from Guiding Star in Mor Dhona (X:21.9, Y:6.9).",
   "Three stages: Mastercraft (IL55) → Supra (IL75) → Lucis (IL90)."),
  "Mastercraft": N(
   "Base IL55 tools from your level 50 Disciple of the Hand or Land class quest.",
   "Each crafter tool is a level 50 three-star craft - no relic currency needed yet."),
  "Supra": N(
   "IL75 tools (patch 2.3) from Talan in Mor Dhona (X:22, Y:6). Every class also turns in 2 Fieldcraft Demimateria III and 1 Mastercraft Demimateria.",
   "[[Crafters]]",
   "1. Craft the class item from a gathered material (mostly an Unspoiled Node item).",
   "2. Combine it with the 2 Fieldcraft Demimateria III into the final turn-in.",
   "[[Gatherers]]",
   "Hand in a Talan Seal instead:",
   "- Miner: 30 Antumbral Rock",
   "- Botanist: 30 Redolent Log",
   "[[Fisher]]",
   "Catch 1 Olgoi-Khorkhoi: hook a Storm Rider, then mooch it during Heat Waves."),
  "Lucis": N(
   "IL90 tools (patch 2.5) from Talan. Every class trades its Supra tool plus 5 Moonstone (about 4,000 GC seals each, 20,000 total).",
   "Tip: once you have one crafter Lucis and one gatherer Lucis, a Calamity Salvager sells the other tools in that category for 1,000 gil each - e.g. Miner Lucis unlocks Botanist and Fisher; one crafter Lucis unlocks all crafter tools.",
   "[[Crafters]]",
   "Craft the class-specific final item (~36,500 GC seals all-in; Weaver ~37,400, Culinarian ~29,150).",
   "[[Gatherers]]",
   "Hand in a Talan Mastery Seal:",
   "- Miner: 15 Magicked Oilcloth + 30 Antumbral Rock",
   "- Botanist: 15 Bamboo Paper + 30 Redolent Log",
   "[[Fisher]]",
   "The Mastery Seal needs 15 Glacier Crystal + 10 Gigant Clam."),
 },
 "Skysteel Tools": {
  "_line": N(
   "Heavensward Disciple of the Hand & Land tools, built at the Skysteel Manufactory in the Firmament. Hand-ins go to Denys (X:8.0, Y:10.0), collectables to Spanner, quest steps to Nimie and Emeny.",
   "Six stages: Skysteel → Skysteel +1 → Dragonsung → Augmented Dragonsung → Skysung → Skybuilders'.",
   "Tip: from Augmented Dragonsung on, the current tool must be equipped while you gather or fish the upgrade items."),
  "Skysteel": N(
   "Base IL440 tools.",
   "1. Finish 'Mislaid Plans' for a Skysteel Prototype Coffer - it becomes your current class's tool.",
   "2. Other classes' base tools cost 80,000 gil from Denys."),
  "Skysteel +1": N(
   "First upgrade (IL455), handed to Denys.",
   "[[Crafters]]",
   "Craft 20 of the tool's first-tier collectable. Each uses one 'Oddly Specific' material (20 Purple Crafters' Scrips at a Scrip Exchange, or Market Board; ~400 scrips per tool) plus a gathered item.",
   "Tip: Atrociraptor Skin and Vampire Vine Sap aren't gatherable (Hunting ventures or Bicolor traders).",
   "[[Gatherers]]",
   "Gather 340 first-tier items + 120 hidden items from the same nodes.",
   "[[Fisher]]",
   "Catch 40 Thinker's Coral at the Thaliak River, Dravanian Hinterlands."),
  "Dragonsung": N(
   "Second upgrade (IL475), handed to Denys, then finish the one-time quest 'In Everyone's Best Interests'.",
   "[[Crafters]]",
   "Craft 30 of the second-tier recipe: 'Oddly Specific' mats (~600 Purple Crafters' Scrips), gathered mats and an intermediate craft.",
   "Tip: Zonure Skin and Ovim Fleece come from Hunting ventures or Bicolor traders; Ala Mhigan Salt Crystal is a timed node in The Lochs.",
   "[[Gatherers]]",
   "Gather 510 second-tier items + 180 hidden.",
   "[[Fisher]]",
   "Catch 60 Dragonspine at Dragonspit, Coerthas Western Highlands."),
  "Augmented Dragonsung": N(
   "Third upgrade (IL485) - talk to Nimie (tool equipped or in your bag) for 'Ever Skyward'. From here the tool must be equipped while gathering or fishing.",
   "[[Crafters]]",
   "1. Craft 18 third-tier collectables at max collectability (each ~20 Purple/Skybuilders' Scrips).",
   "2. Turn them in to Denys for 90 gobbiegoo.",
   "3. Give the gobbiegoo to Nimie.",
   "Tip: collectability returns 2/3/5 gobbiegoo per craft, so 18 crafts best case, 45 worst.",
   "[[Gatherers]]",
   "1. Gather 500 third-tier + 180 hidden items.",
   "2. Turn the third-tier items in for 50 gobbiegoo.",
   "3. Hand the gobbiegoo and the 180 hidden items to Nimie.",
   "[[Fisher]]",
   "1. Catch 60 Petal Shell at Plum Spring, Yanxia.",
   "2. Turn them in for 6 Fisher's Gobbiegoo and give those to Nimie."),
  "Skysung": N(
   "Fourth upgrade (IL500) - talk to Nimie for 'The Tools of Tomorrow', then finish 'Oddness in the End'.",
   "[[Crafters]]",
   "1. Craft 21 fourth-tier collectables at max collectability (~420 Purple or Skybuilders' Scrips best case, 1,060 worst).",
   "2. Turn them in for 105 highly viscous gobbiegoo and give it to Nimie.",
   "[[Gatherers]]",
   "1. Gather 600 fourth-tier + 200 hidden items.",
   "2. Turn the fourth-tier items in for 60 highly viscous gobbiegoo and hand it to Nimie with the 200 hidden items.",
   "[[Fisher]]",
   "1. Catch 70 Allagan Hunter at the Alpha Quadrant, Azys Lla.",
   "2. Turn them in for 7 Highly Viscous Fisher's Gobbiegoo and give those to Nimie."),
  "Skybuilders'": N(
   "Final upgrade (IL510) - talk to Emeny for 'The Pinnacle of Possibility'. Collectables go to Spanner, then Emeny.",
   "[[Crafters]]",
   "1. Craft 20 fifth-tier collectables at max collectability (Expert Recipes; fixed macros work).",
   "2. Turn them in for 60 oddly delicate parts and give them to Emeny.",
   "Cost: ~400 Purple / 600 Skybuilders' Scrips best case, 1,200 / 1,800 worst. Other mats are gathered in The Diadem.",
   "[[Gatherers]]",
   "1. Gather 36 fifth-tier collectables + 750 Diadem items (Diadem central isle).",
   "2. Turn them in for 250 oddly delicate parts + 25 inconceivably delicate parts and give them to Emeny.",
   "[[Fisher]]",
   "1. Catch 50 each of Flintstrike and Pickled Pom collectables at max collectability.",
   "2. Turn them in for 200 Oddly Delicate Fishing Rod Parts + 200 Fishing Reel Parts and give them to Emeny."),
 },
 "Resplendent Tools": {
  "_line": N(
   "Shadowbringers Disciple of the Hand & Land tools - a single Resplendent upgrade.",
   "Crafters and gatherers earn theirs in completely different ways."),
  "Resplendent": N(
   "[[Crafters]]",
   "Craft up a chain, handing each component in to Limbeth:",
   "1. Buy Material A (25 Purple Crafters' Scrips each).",
   "2. Craft it into Component A and turn that in for Material B.",
   "3. Craft 2 Material B into Component B and turn it in for Material C.",
   "4. Craft 2 Material C into Component C and turn it in for Final Material.",
   "5. Trade 60 Final Material for the tool.",
   "Tip: at max collectability every step you need about 30 Material A per class (~750 scrips); at minimum up to 240 (~6,000).",
   "[[Gatherers]]",
   "Earned from gathering-log achievements - no materials:",
   "- Miner: record 220 unique miner-only items (I Found That: Miner VII → Resplendent Minefiend's Pickaxe)",
   "- Botanist: record 340 (Botanist VII → Fieldfiend's Hatchet)",
   "Tip: items from any expansion count, including Legendary Node items (Tomes of Regional Folklore).",
   "[[Fisher]]",
   "Catch and record 1,140 unique fish (I Caught That VII → Resplendent Tacklefiend's Rod)."),
 },
 "Splendorous Tools": {
  "_line": N(
   "Endwalker Disciple of the Hand & Land tools, built at the Crystarium. Crafters hand collectables to Quinnana (X:10.4, Y:7.7) and upgrade items to Chora-Zoi (X:10.5, Y:7.7).",
   "Seven stages: Splendorous → Augmented → Crystalline → Chora-Zoi's → Brilliant → Vrandtic → Lodestar.",
   "Tip: gatherers and the Fisher must have the current tool equipped while gathering or fishing (the Fisher uses a Select Bait Ball from Quinnana)."),
  "Splendorous": N(
   "Base IL570 tools.",
   "1. Finish 'An Original Improvement' for a Splendorous Coffer - it becomes your current class's tool.",
   "2. Other classes' base tools cost 750 scrips from Quinnana in the Crystarium."),
  "Augmented": N(
   "First upgrade (IL590) - repeatable quest 'A Dedicated Tool'.",
   "[[Crafters]]",
   "Craft 20 first-tier collectables, each from a 'Select' material (25 Purple Crafters' Scrips) plus gathered mats - ~500 scrips best case, 1,500 worst.",
   "Tip: Almasty Fur and Petalouda Scales aren't gatherable (Hunting ventures or Bicolor traders).",
   "[[Gatherers]]",
   "Gather 60 of your collectable plus 180 hidden Splendorous Water/Earth Shards.",
   "[[Fisher]]",
   "Catch 30 each of Platinum Seahorse and Clavekeeper."),
  "Crystalline": N(
   "Second upgrade (IL620) - repeatable quest 'An Adaptive Tool'.",
   "[[Crafters]]",
   "Craft 30 second-tier collectables (25 Purple Crafters' Scrips per 'Select' item; ~750 best case, 2,250 worst).",
   "Tip: Ophiotauros Hide and Lunatender Blossom come from Hunting ventures or Bicolor traders.",
   "[[Gatherers]]",
   "Gather 70 of your collectable plus 210 hidden Adaptive Fire/Lightning Crystals.",
   "[[Fisher]]",
   "Catch 40 each of Mirror Image and Spangled Pirarucu."),
  "Chora-Zoi's": N(
   "Third upgrade (IL625) - repeatable quest 'A Tool of Her Own'.",
   "[[Crafters]]",
   "Craft 30 third-tier collectables (30 Purple Crafters' Scrips per 'Select' item; ~900 best case, 2,700 worst).",
   "[[Gatherers]]",
   "Gather 70 of your collectable plus 210 hidden Custom Ice/Wind Crystals.",
   "Tip: Aetherial Reduction also yields the components.",
   "[[Fisher]]",
   "Catch 40 each of Gold Dustfish and Forgiven Melancholy."),
  "Brilliant": N(
   "Fourth upgrade (IL630) - repeatable quest 'A Tool without Compare'.",
   "[[Crafters]]",
   "Craft 30 fourth-tier collectables (30 Purple Crafters' Scrips per 'Select' item; ~900 best case, 2,700 worst).",
   "[[Gatherers]]",
   "Gather 70 of your collectable plus 210 hidden Brilliant Lightning/Earth Clusters (or use Aetherial Reduction).",
   "[[Fisher]]",
   "Catch 40 each of Oil Slick and Gonzalo's Grace."),
  "Vrandtic": N(
   "Fifth upgrade (IL635) - repeatable quest 'A Tool for the Ages'.",
   "[[Crafters]]",
   "Craft 20 fifth-tier collectables (Expert recipes; 35 Purple Crafters' Scrips per 'Select' item; ~700 best case, 2,100 worst).",
   "[[Gatherers]]",
   "Gather ~74 of your collectable plus 220 hidden Inspirational Wind/Fire Clusters.",
   "Tip: ~74 is the best case via Aetherial Reduction; turning in to Quinnana takes 110.",
   "[[Fisher]]",
   "Catch 43 each of Deadwood Shadow and Ronkan Bullion."),
  "Lodestar": N(
   "Final upgrade (IL640) - repeatable quest 'Stand Tool, My Friend'.",
   "[[Crafters]]",
   "Craft 20 sixth-tier collectables (Expert recipes; 35 Purple Crafters' Scrips per 'Select' item; ~700 best case, 2,100 worst).",
   "[[Gatherers]]",
   "Gather ~74 of your collectable plus 220 hidden Nightforged Ice/Water Clusters.",
   "Tip: ~74 is the best case via Aetherial Reduction; turning in to Quinnana takes 110.",
   "[[Fisher]]",
   "Catch 43 each of Little Bounty and Saint Fathric's Face."),
 },
 "Cosmic Tools": {
  "_line": N(
   "Dawntrail Disciple of the Hand & Land tools (patch 7.2), upgraded entirely through Cosmic Exploration - nothing to craft, gather or catch.",
   "1. Get the first prototype from a Cosmic Prototype Coffer (quest 'A Cosmic Homecoming'). Later class prototypes are free from Researchingway in the Cosmic zones (Sinus Ardorum, Phaenna, Oizys, Auxesia).",
   "2. Earn per-class research data (Types I-VII) from stellar missions.",
   "3. Talk to Researchingway to upgrade. Tools climb a 19-step ladder from level 10 (ilvl 10) to level 100 (ilvl 780).",
   "Tip: finishing a tool's final tier stage gives a research bonus for the rest of that tier (+50% for 1-2 tools done, +100% for 3-4, +150% for 5+), so finish one tool at a time and do crafters before gatherers.",
   "The Overview and Relic tabs still track your Cosmic progress from FFXIV Collect."),
  "Cosmic": N(
   "Novice tier - the base prototype through v0.2-v0.8 to the Cosmic tool, using Type I-IV research data (per class, cumulative).",
   "Needed to reach Cosmic:",
   "- about 2,800 Type I, 2,900 Type II, 2,300 Type III, 1,500 Type IV",
   "Tip: the tool doesn't need to be equipped to earn data, but equipping it for a whole mission adds a zone-credit bonus.",
   "[[Crafters]]",
   "Recommended on Oizys: class D, then class C missions.",
   "[[Gatherers]]",
   "Recommended on Oizys: the class A 'Mineral Gravitational Stabilizers' (Miner) / 'Fibrous Gravitational Stabilizers' (Botanist).",
   "[[Fisher]]",
   "Recommended: 'Northwestern Water Inspection' and 'Absolute Specimen', then 'Aquatic Inspection I'."),
  "Stellar": N(
   "Intermediate tier - v1.1-v1.4 up to the Stellar tool, now also needing Type V data.",
   "Cumulative totals at Stellar:",
   "- about 4,700 each of Types I-III, 4,000 Type IV, 1,500 Type V",
   "[[Crafters]]",
   "Recommended: class C, then class B missions.",
   "[[Gatherers]]",
   "Stay on class A, dipping into class B if Type V lags."),
  "Hyper": N(
   "Advanced tier - v1.1-v1.2 up to the Hyper tool, adding Type VI data.",
   "Cumulative totals at Hyper:",
   "- about 7,100 each of Types I-IV, 3,150 Type V, 1,900 Type VI",
   "[[Crafters]]",
   "Recommended: class B, then class A.",
   "Tip: Type VI is only on class A and is the longest grind.",
   "[[Gatherers]]",
   "Focus class A 'Gravitational Stabilizers', using class B if Type V lags."),
  "Stellar (final)": N(
   "Expert tier - the final level 100 / ilvl 780 tool (the Stars stage). v1.1-v1.2 up to Stars, adding Type VII data.",
   "Cumulative totals at Stars:",
   "- about 9,500 each of Types I-IV, 5,600 Type V, 2,700 Type VI, 1,600 Type VII",
   "This is the current end of the line; further upgrades come in later patches."),
 },
}

path=os.path.join(os.path.dirname(__file__),"extracted","relic_step_notes.json")
notes=json.load(open(path,encoding="utf-8"))
notes.update(TOOL_NOTES)
json.dump(notes,open(path,"w",encoding="utf-8"),ensure_ascii=False,indent=2)
print("Updated tool notes:", ", ".join(TOOL_NOTES))
