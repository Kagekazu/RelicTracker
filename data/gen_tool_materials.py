"""
Generates data/extracted/tool_extra_materials.json — the curated material source for relic
weapons and DoH/DoL relic tools. One row per (step, material, discipline);
the `jobs` flag picks the relic job slot (0-7 crafters, 8 MIN, 9 BTN, 10 FSH). For each crafter
step we capture all three items the wiki lists: the Purple-Scrip currency item, the gathered
input(s), and the trade/collectable turn-in. Collectability-gated steps use best-case (max
collectability) counts. Re-run this after adding a tool chain; do not hand-edit the JSON.
"""
import json, os

SLOT = {"CRP":0,"BSM":1,"ARM":2,"GSM":3,"LTW":4,"WVR":5,"ALC":6,"CUL":7,"MIN":8,"BTN":9,"FSH":10}
def flags(*slots):
    a=[False]*11
    for s in slots: a[s]=True
    return a
ALL=[True]*11
rows=[]
def add(step, material, per, *slots):
    rows.append({"step":step,"material":material,"perUnit":per,"jobs":flags(*slots)})

# ---------------- ARR Lucis shared currencies (kept) ----------------
rows.append({"step":"Supra","material":"Fieldcraft Demimateria III","perUnit":2,"jobs":ALL})
rows.append({"step":"Supra","material":"Mastercraft Demimateria","perUnit":1,"jobs":ALL})
rows.append({"step":"Lucis","material":"Moonstone","perUnit":5,"jobs":ALL,"purchase":{"currency":"GC seals","unit":4000}})

C=["CRP","BSM","ARM","GSM","LTW","WVR","ALC","CUL"]
def crafter(step, cur_cnt, trade_cnt, data):
    for cls in C:
        cur, gaths, trade = data[cls]
        s=SLOT[cls]
        add(step, cur, cur_cnt, s)
        for gname,gcnt in gaths:
            add(step, gname, gcnt, s)
        add(step, trade, trade_cnt, s)

# ===== Skysteel +1 (IL455) — craft 20 =====
crafter("Skysteel +1",20,20,{
 "CRP":("Oddly Specific Petrified Log",[("White Ash Log",20)],"Oddly Specific Petrified Orb"),
 "BSM":("Oddly Specific Iron Sand",[("Manasilver Sand",20)],"Oddly Specific Rivets"),
 "ARM":("Oddly Specific Iron Ore",[("Manasilver Sand",20)],"Oddly Specific Wire"),
 "GSM":("Oddly Specific Uncut Gemstone",[("Manasilver Sand",20)],"Oddly Specific Whetstone"),
 "LTW":("Oddly Specific Skin",[("Atrociraptor Skin",20)],"Oddly Specific Leather"),
 "WVR":("Oddly Specific Cotton",[("Pixie Floss Boll",20)],"Oddly Specific Moonbeam Silk"),
 "ALC":("Oddly Specific Quartz",[("Vampire Vine Sap",20)],"Oddly Specific Synthetic Resin"),
 "CUL":("Oddly Specific Seeds",[("Highland Spring Water",20)],"Oddly Specific Seed Extract"),
})
add("Skysteel +1","Oddly Specific Obsidian",340,SLOT["MIN"]); add("Skysteel +1","Oddly Specific Mineral Sand",120,SLOT["MIN"])
add("Skysteel +1","Oddly Specific Latex",340,SLOT["BTN"]); add("Skysteel +1","Oddly Specific Fossil Dust",120,SLOT["BTN"])
add("Skysteel +1","Thinker's Coral",40,SLOT["FSH"])

# ===== Dragonsung (IL475) — craft 30 =====
crafter("Dragonsung",30,30,{
 "CRP":("Oddly Specific Petrified Log",[("Sandteak Log",120)],"Oddly Specific Shaft"),
 "BSM":("Oddly Specific Iron Sand",[("Titancopper Ore",120),("Titanium Ore",30)],"Oddly Specific Fitting"),
 "ARM":("Oddly Specific Iron Ore",[("Titancopper Ore",120),("Titanium Ore",30)],"Oddly Specific Chainmail Sheet"),
 "GSM":("Oddly Specific Uncut Gemstone",[("Volcanic Tuff",90)],"Oddly Specific Gemstone"),
 "LTW":("Oddly Specific Skin",[("Zonure Skin",120),("Yellow Alumen",30)],"Oddly Specific Vellum"),
 "WVR":("Oddly Specific Cotton",[("Ovim Fleece",120),("Extra Effervescent Water",30),("Ala Mhigan Salt Crystal",30)],"Oddly Specific Velvet"),
 "ALC":("Oddly Specific Quartz",[("Extra Effervescent Water",30),("Ala Mhigan Salt Crystal",30)],"Oddly Specific Glass"),
 "CUL":("Oddly Specific Seeds",[("Royal Grapes",50)],"Oddly Specific Seed Flour"),
})
add("Dragonsung","Oddly Specific Dark Matter",510,SLOT["MIN"]); add("Dragonsung","Oddly Specific Striking Stone",180,SLOT["MIN"])
add("Dragonsung","Oddly Specific Amber",510,SLOT["BTN"]); add("Dragonsung","Oddly Specific Bauble",180,SLOT["BTN"])
add("Dragonsung","Dragonspine",60,SLOT["FSH"])

# ===== Augmented Dragonsung (IL485) — best case 18 (gathered 36) =====
crafter("Augmented Dragonsung",18,18,{
 "CRP":("Oddly Specific Cedar Log",[("Lignum Vitae Log",36)],"Oddly Specific Cedar Lumber"),
 "BSM":("Oddly Specific Coerthan Iron Ore",[("Dimythrite Ore",36)],"Oddly Specific Iron Nails"),
 "ARM":("Oddly Specific Mythrite Sand",[("Dimythrite Ore",36)],"Oddly Specific Mythril Rings"),
 "GSM":("Oddly Specific Silver Ore",[("Dimythrite Sand",36)],"Oddly Specific Silver Nugget"),
 "LTW":("Oddly Specific Gagana Skin",[("Yellow Alumen",36)],"Oddly Specific Gaganaskin Strap"),
 "WVR":("Oddly Specific Fleece",[("Dwarven Cotton Boll",36)],"Oddly Specific Cloth"),
 "ALC":("Oddly Specific Sap",[("Vampire Cup Vine",36)],"Oddly Specific Glue"),
 "CUL":("Oddly Specific Aloe",[("Frantoio",36)],"Oddly Specific Oil"),
})
add("Augmented Dragonsung","Oddly Specific Schorl",500,SLOT["MIN"]); add("Augmented Dragonsung","Oddly Specific Landborne Aethersand",180,SLOT["MIN"])
add("Augmented Dragonsung","Oddly Specific Dark Chestnut Log",500,SLOT["BTN"]); add("Augmented Dragonsung","Oddly Specific Leafborne Aethersand",180,SLOT["BTN"])
add("Augmented Dragonsung","Petal Shell",60,SLOT["FSH"])

# ===== Skysung (IL500) — best case 21 =====
crafter("Skysung",21,21,{
 "CRP":("Oddly Specific Cedar Log",[("Lignum Vitae Log",84)],"Oddly Specific Cedar Plank"),
 "BSM":("Oddly Specific Coerthan Iron Ore",[("Dimythrite Ore",84),("Mythrite Ore",21)],"Oddly Specific Iron Ingot"),
 "ARM":("Oddly Specific Mythrite Sand",[("Dimythrite Ore",84),("Mythrite Ore",21)],"Oddly Specific Mythril Plate"),
 "GSM":("Oddly Specific Silver Ore",[("Dimythrite Sand",84),("Mythrite Sand",21)],"Oddly Specific Silver Ingot"),
 "LTW":("Oddly Specific Gagana Skin",[("Sea Swallow Skin",84),("Yellow Alumen",21)],"Oddly Specific Gagana Leather"),
 "WVR":("Oddly Specific Fleece",[("Dwarven Cotton Boll",84)],"Oddly Specific Undyed Woolen Cloth"),
 "ALC":("Oddly Specific Sap",[("Extra Effervescent Water",21),("Ala Mhigan Salt Crystal",21)],"Oddly Specific Rubber"),
 "CUL":("Oddly Specific Aloe",[("Royal Grapes",35)],"Oddly Specific Paste"),
})
add("Skysung","Oddly Specific Primordial Ore",600,SLOT["MIN"]); add("Skysung","Oddly Specific Primordial Asphaltum",200,SLOT["MIN"])
add("Skysung","Oddly Specific Primordial Log",600,SLOT["BTN"]); add("Skysung","Oddly Specific Primordial Resin",200,SLOT["BTN"])
add("Skysung","Allagan Hunter",70,SLOT["FSH"])

# ===== Skybuilders' (IL510) — best case 20 (gathered 100 each, Diadem) =====
AG="Approved Grade 4 Artisanal Skybuilders' "
crafter("Skybuilders'",20,20,{
 "CRP":("Oddly Delicate Pine Log",[(AG+"Log",100),(AG+"Barbgrass",100)],"Oddly Delicate Pine Lumber"),
 "BSM":("Oddly Delicate Silvergrace Ore",[(AG+"Cloudstone",100),(AG+"Silex",100)],"Oddly Delicate Silver Gear"),
 "ARM":("Oddly Delicate Scheelite",[(AG+"Cloudstone",100),(AG+"Prismstone",100)],"Oddly Delicate Wolfram Square"),
 "GSM":("Oddly Delicate Raw Celestine",[(AG+"Silex",100),(AG+"Barbgrass",100)],"Oddly Delicate Celestine"),
 "LTW":("Oddly Delicate Gazelle Hide",[(AG+"Log",100),(AG+"Caiman",100)],"Oddly Delicate Gazelle Leather"),
 "WVR":("Oddly Delicate Rhea",[(AG+"Cocoon",100),(AG+"Ice Stalagmite",100)],"Oddly Delicate Rhea Cloth"),
 "ALC":("Oddly Delicate Mistletoe",[(AG+"Spring Water",100),(AG+"Raspberry",100)],"Oddly Delicate Holy Water"),
 "CUL":("Oddly Delicate Hammerhead Shark",[(AG+"Spring Water",100),(AG+"Ice Stalagmite",100)],"Oddly Delicate Shark Oil"),
})
add("Skybuilders'","Oddly Delicate Adamantite Ore",36,SLOT["MIN"]); add("Skybuilders'","Oddly Delicate Raw Jade",750,SLOT["MIN"])
add("Skybuilders'","Oddly Delicate Feather",36,SLOT["BTN"]); add("Skybuilders'","Oddly Delicate Birch Log",750,SLOT["BTN"])
add("Skybuilders'","Flintstrike",50,SLOT["FSH"]); add("Skybuilders'","Pickled Pom",50,SLOT["FSH"])

# ===== Splendorous Tools (EW) — part 1: Augmented + Crystalline crafters =====
# Splendorous (base IL570) = note only (Splendorous Coffer from the quest; extras 750 scrips).
# Augmented (IL590): craft 20 first-tier collectables -> 60 splendorous components. Best case 20.
crafter("Augmented",20,20,{
 "CRP":("Select Ironwood Lumber",[("Ironwood Log",40)],"Connoisseur's Chair"),
 "BSM":("Select Manganese Ingot",[("Phrygian Gold Ore",40)],"Connoisseur's Wall Lantern"),
 "ARM":("Select Titanium Plate",[("Ironwood Log",40)],"Connoisseur's Ornate Door"),
 "GSM":("Select Crystal Glass",[("Bismuth Ore",40)],"Connoisseur's Vanity Mirror"),
 "LTW":("Select Smilodon Leather",[("Almasty Fur",40)],"Connoisseur's Rug"),
 "WVR":("Select Scarlet Moko Cloth",[("Almasty Fur",40)],"Connoisseur's Swag Valance"),
 "ALC":("Select Hoptrap Leaf",[("Petalouda Scales",40)],"Connoisseur's Cosmetics Box"),
 "CUL":("Select Pixieberry",[("Sideritis Leaves",40)],"Connoisseur's Pixieberry Tea"),
})
# Crystalline (IL620): craft 30 second-tier collectables -> 90 adaptive components. Best case 30.
# (Intermediate "crafted" column skipped, same as Skysung.)
crafter("Crystalline",30,30,{
 "CRP":("Select Ironwood Lumber",[("Integral Log",150),("Chondrite",150),("Dimythrite Ore",30)],"Connoisseur's Marimba"),
 "BSM":("Select Manganese Ingot",[("Raw Star Quartz",90),("Annite",120),("Chondrite",150),("Dimythrite Ore",30)],"Connoisseur's Coffee Brewer"),
 "ARM":("Select Titanium Plate",[("Integral Log",150),("Chondrite",150),("Dimythrite Ore",30)],"Connoisseur's Bench"),
 "GSM":("Select Crystal Glass",[("Raw Star Quartz",90),("Annite",120),("Chondrite",150),("Dimythrite Ore",30)],"Connoisseur's Astroscope"),
 "LTW":("Select Smilodon Leather",[("Integral Log",150),("AR-Caean Cotton Boll",150)],"Connoisseur's Leather Sofa"),
 "WVR":("Select Scarlet Moko Cloth",[("AR-Caean Cotton Boll",150),("Ophiotauros Hide",120),("Eblan Alumen",30)],"Connoisseur's Red Carpet"),
 "ALC":("Select Hoptrap Leaf",[("Underground Spring Water",80),("Lunatender Blossom",40),("Lime Basil",40),("Tiger Lily",40)],"Connoisseur's Elixir Bottle"),
 "CUL":("Select Pixieberry",[("Dark Rye",120),("Palm Syrup",120)],"Connoisseur's Pixieberry Tart"),
})

# Chora-Zoi's (IL625): craft 30 -> 90 customized components. Best case 30.
crafter("Chora-Zoi's",30,30,{
 "CRP":("Select Dark Chestnut Lumber",[("Red Pine Log",150),("Flax",60),("Cotton Boll",30),("Beehive Chip",90)],"Connoisseur's Picture Frame"),
 "BSM":("Select Bismuth Ingot",[("Pewter Ore",150),("Tin Ore",30),("Doman Iron Ore",120),("Iron Ore",30)],"Connoisseur's Samurai Blade"),
 "ARM":("Select Cobalt Plate",[("Manganese Ore",150),("Molybdenum Ore",30),("Silex",90),("Effervescent Water",30),("Rock Salt",30),("Minium",30)],"Connoisseur's Aquarium"),
 "GSM":("Select Bluespirit Tile",[("Pewter Ore",150),("Tin Ore",30),("Manasilver Sand",120),("Silver Ore",30)],"Connoisseur's Glaives"),
 "LTW":("Select Green Glider Leather",[("Kumbhira Skin",120),("Eblan Alumen",30),("Iridescent Cocoon",120),("Effervescent Water",30)],"Connoisseur's Varsity Shoes"),
 "WVR":("Select Waterproof Cloth",[("Manganese Ore",150),("Molybdenum Ore",30),("Flax",60),("Cotton Boll",30),("Beehive Chip",90)],"Connoisseur's Linen Parasol"),
 "ALC":("Select Rak'tika Seedling",[("Underground Spring Water",90),("Lunatender Blossom",30),("Fernleaf Lavender",60),("Hoptrap Leaf",60),("Vampire Vine Sap",60)],"Connoisseur's Topiary Moogle"),
 "CUL":("Select Squid Ink",[("Sharlayan Rock Salt",180),("Ovibos Milk",150),("Highland Wheat",100),("Dravanian Spring Water",20),("Abalathian Rock Salt",20)],"Connoisseur's Squid Baguette"),
})
# Brilliant (IL630): craft 30 -> 90 brilliant components. Best case 30.
crafter("Brilliant",30,30,{
 "CRP":("Select Dark Chestnut Lumber",[("Integral Log",150),("Ironwood Log",150),("Manganese Ore",150),("Molybdenum Ore",30)],"Connoisseur's Bookshelf"),
 "BSM":("Select Bismuth Ingot",[("Chondrite",150),("Dimythrite Ore",30),("Manganese Ore",150),("Molybdenum Ore",30),("Phrygian Gold Ore",150),("Zinc Ore",30)],"Connoisseur's Chandelier"),
 "ARM":("Select Cobalt Plate",[("Chondrite",150),("Dimythrite Ore",30),("Manganese Ore",150),("Molybdenum Ore",30),("Ironwood Log",150)],"Connoisseur's Escutcheon"),
 "GSM":("Select Bluespirit Tile",[("Raw Star Quartz",90),("Annite",120),("Phrygian Gold Ore",150),("Zinc Ore",30),("Manganese Ore",150),("Molybdenum Ore",30)],"Connoisseur's Baghnakhs"),
 "LTW":("Select Green Glider Leather",[("Ophiotauros Hide",120),("Eblan Alumen",30),("Phrygian Gold Ore",150),("Zinc Ore",30),("Apkallu Down",60)],"Connoisseur's Drinking Apkallu"),
 "WVR":("Select Waterproof Cloth",[("AR-Caean Cotton Boll",150),("Scarlet Moko Grass",150),("Saiga Hide",120),("Eblan Alumen",30)],"Connoisseur's Jacket"),
 "ALC":("Select Rak'tika Seedling",[("Ironwood Log",150),("Underground Spring Water",120),("Lunatender Blossom",30),("Lime Basil",60),("Hoptrap Leaf",120),("Vampire Vine Sap",120)],"Connoisseur's Planter Partition"),
 "CUL":("Select Squid Ink",[("Thavnairian Perilla Leaf",180),("Ovibos Milk",150),("Highland Wheat",100),("Dravanian Spring Water",20),("Abalathian Rock Salt",20)],"Connoisseur's Spaghetti al Nero"),
})
# Vrandtic Visionary's (IL635): craft 20 -> 60 inspirational components. Best case 20. Expert recipes.
crafter("Vrandtic",20,20,{
 "CRP":("Select Bamboo Stick",[("Integral Log",20),("Limestone",20)],"Connoisseur's Bamboo Fence"),
 "BSM":("Select Duraluminum Ingot",[("Chondrite",20),("Raw Star Quartz",20)],"Connoisseur's Rousing Chronometer"),
 "ARM":("Select Brashgold Plate",[("Truegold Ore",20),("Horse Chestnut Log",20)],"Connoisseur's Trumpet"),
 "GSM":("Select Marble",[("Pewter Ore",20),("Bismuth Ore",20)],"Connoisseur's Washbasin"),
 "LTW":("Select Chalicotherium Leather",[("Horse Chestnut Log",20),("Manganese Ore",20)],"Connoisseur's Targe"),
 "WVR":("Select Duskcourt Cloth",[("Ruby Cotton Boll",20),("AR-Caean Cotton Boll",20)],"Connoisseur's Petasos"),
 "ALC":("Select Cudweed",[("Lunatender Blossom",20),("Mousse Flesh",20)],"Connoisseur's Lunar Curtain"),
 "CUL":("Select Blue Crab",[("Upland Wheat",20),("Egg of Elpis",20)],"Connoisseur's Crab Cakes"),
})
# Lodestar (IL640): craft 20 -> 60 nightforged components. Best case 20. Expert recipes (final).
crafter("Lodestar",20,20,{
 "CRP":("Select Bamboo Stick",[("Ambrosial Water",20),("Siltstone",20),("Granite",20)],"Connoisseur's Shishi-odoshi"),
 "BSM":("Select Duraluminum Ingot",[("Integral Log",20),("Pewter Ore",20),("Wildfowl Feather",20)],"Connoisseur's Retainer Bell"),
 "ARM":("Select Brashgold Plate",[("Mahogany Log",20),("Bismuth Ore",20),("Manganese Ore",20)],"Connoisseur's Marching Horn"),
 "GSM":("Select Marble",[("Ambrosial Water",20),("Annite",20),("Granite",20)],"Connoisseur's Marble Fountain"),
 "LTW":("Select Chalicotherium Leather",[("Ruby Cotton Boll",20),("Almasty Fur",20),("AR-Caean Cotton Boll",20)],"Connoisseur's Leather Jacket"),
 "WVR":("Select Duskcourt Cloth",[("Almasty Fur",20),("AR-Caean Cotton Boll",20),("Scarlet Moko Grass",20)],"Connoisseur's Fat Cat Sofa"),
 "ALC":("Select Cudweed",[("Petalouda Scales",20),("Berkanan Sap",20),("Amra",20)],"Connoisseur's Tincture of Vitality"),
 "CUL":("Select Blue Crab",[("Thavnairian Perilla Leaf",20),("Blood Tomato",20),("Pearl Ginger",20)],"Connoisseur's Chili Crab"),
})
# Splendorous Miner/Botanist: one collectable each per step (count = collectables gathered; best
# case via Aetherial Reduction for Vrandtic/Lodestar). Hidden "shard/crystal" pairs -> notes.
SPL_GATH={
 "Augmented":("Connoisseur's Prismstone","Connoisseur's Wattle Petribark",60),
 "Crystalline":("Connoisseur's Red Malachite","Connoisseur's Levin Mint",70),
 "Chora-Zoi's":("Connoisseur's Soiled Femur","Connoisseur's Miracle Apple",70),
 "Brilliant":("Connoisseur's Aurum Regis Ore","Connoisseur's Cloves",70),
 "Vrandtic":("Connoisseur's Asphaltum","Connoisseur's Gianthive Chip",74),
 "Lodestar":("Connoisseur's Raw Onyx","Connoisseur's Glimshroom",74),
}
for st,(mi,bo,cnt) in SPL_GATH.items():
    add(st,mi,cnt,SLOT["MIN"]); add(st,bo,cnt,SLOT["BTN"])
# Splendorous Fisher: two fish per step (count = caught of each, best case).
SPL_FISH={
 "Augmented":[("Platinum Seahorse",30),("Clavekeeper",30)],
 "Crystalline":[("Mirror Image",40),("Spangled Pirarucu",40)],
 "Chora-Zoi's":[("Gold Dustfish",40),("Forgiven Melancholy",40)],
 "Brilliant":[("Oil Slick",40),("Gonzalo's Grace",40)],
 "Vrandtic":[("Deadwood Shadow",43),("Ronkan Bullion",43)],
 "Lodestar":[("Little Bounty",43),("Saint Fathric's Face",43)],
}
for st,fishes in SPL_FISH.items():
    for fname,fcnt in fishes: add(st,fname,fcnt,SLOT["FSH"])

# ===== Resplendent Tools (ShB) — single "Resplendent" step =====
# Crafters: buy Material A (25 Purple Crafters' Scrips each) and craft up the chain
# (Component A -> Material B -> Component B -> Material C -> Component C -> Final Material);
# 60 Final Material buys the tool. Best case (max collectability) = 30 Material A/class.
# Intermediates (Component A/B/C, Material B/C) are 1-2 per turn-in (RNG) and transient, so we
# track only the two stable endpoints: Material A (the scrip buy) and Final Material (the gate).
# Gatherers earn their tools from gathering-log achievements (no materials) — note only.
CLASS_NAME={"CRP":"Carpenter","BSM":"Blacksmith","ARM":"Armorer","GSM":"Goldsmith",
            "LTW":"Leatherworker","WVR":"Weaver","ALC":"Alchemist","CUL":"Culinarian"}
for cls in C:
    nm=CLASS_NAME[cls]; s=SLOT[cls]
    add("Resplendent", f"Resplendent {nm}'s Material A", 30, s)
    add("Resplendent", f"Resplendent {nm}'s Final Material", 60, s)

# Cosmic Tools (DT) have NO materials — upgraded via Cosmic Exploration research data (notes only).
# Drop Wyn's placeholder Cosmic/Stellar/Hyper rows so those steps are clean (notes carry them).

# ===== ARR Zodiac weapons (A Relic Reborn) — part 1: Relic / Zenith / Atma / Animus =====
# Activity relic (quests, FATEs, dungeons, books) — notes carry most of it. Wyn's rows were shifted
# one step (12 Atma under Zenith, 9 Books under Atma, Superior Enchanted Ink under Animus), so we
# drop those steps and re-place the stockpilable items at their correct step. Weapon-line `jobs`
# flags are ignored (the Tracker scales by all jobs; the Relic tab shows all step items), so the
# flag array is just a placeholder. Books are held one at a time -> note only, not a row.
arr_rows=[]
def arr_add(step, material, per):
    arr_rows.append({"step":step,"material":material,"perUnit":per,"jobs":[True]*10})
# Relic: base quest turn-in (1 Radz-at-Han Quenching Oil, 15 Poetics).
arr_add("Relic", "Radz-at-Han Quenching Oil", 1)
# Zenith: 3 Thavnairian Mist (20 Poetics each).
arr_add("Zenith", "Thavnairian Mist", 3)
# Atma: 12 FATE-drop atmas (aliased to the 12 items).
arr_add("Atma", "Atma", 12)
# Animus: 9 Trials of the Braves books, held one at a time -> note only.
# Novus: Sphere Scroll = 3 Superior Enchanted Ink + 75 Alexandrite (+ ~75 materia, grade I-IV -> note).
arr_add("Novus", "Superior Enchanted Ink", 3)
arr_add("Novus", "Alexandrite", 75)
# Nexus: Light grind (Soulglaze) -> note only.
# Zodiac (Braves): 4 sub-quests; per weapon = purchased + 8 HQ crafted items.
arr_add("Zodiac", "Bombard Core", 4)          # 20,000 Company Seals each
arr_rows[-1]["purchase"] = {"currency": "GC seals", "unit": 20000}
arr_add("Zodiac", "Sacred Spring Water", 4)   # 200 Poetics each
arr_add("Zodiac", "Furite Sand", 1)           # 100,000 gil
arr_add("Zodiac", "Allagan Resin", 1)         # 100,000 gil
arr_add("Zodiac", "Bronze Lake Crystal", 1)   # 100,000 gil
arr_add("Zodiac", "Brass Kettle", 1)          # 100,000 gil
for craft in ["Perfect Firewood","Perfect Pestle","Perfect Mortar","Furnace Ring",
              "Perfect Vellum","Perfect Cloth","Perfect Pounce","Tailor-made Eel Pie"]:
    arr_add("Zodiac", craft, 1)               # HQ Master Recipe I crafts (desynth ingredients)
# Zeta: 12 Mahatma Light grind (held one at a time) -> note only.

# ===== HW Anima weapons — part 1: Animated / Awoken / Anima / Hyperconductive =====
# Wyn's HW data is scattered across wrong steps (Bone under Awoken, Aether Oil under Complete, etc.)
# so we replace ALL 8 steps and place the stockpilable items correctly. Reconditioned / Sharpened /
# Complete / Lux are left empty until part 2. 13 jobs; flag array is a placeholder (ignored).
hw_rows=[]
def hw_add(step, material, per):
    hw_rows.append({"step":step,"material":material,"perUnit":per,"jobs":[True]*13})
# Animated: 1 of each of the 6 Luminous Crystals (FATE drops -> Astral/Umbral Nodule turn-in).
for c in ["Wind","Fire","Lightning","Ice","Earth","Water"]:
    hw_add("Animated", f"Luminous {c} Crystal", 1)
# Awoken: 10 dungeons in order -> note only.
# Anima: 40 unidentifiable + 16 crafted items.
for u in ["Bone","Shell","Ore","Seeds"]:
    hw_add("Anima", f"Unidentifiable {u}", 10)
for c in ["Adamantite Francesca","Titanium Alloy Mirror","Dispelling Arrow","Kingcake"]:
    hw_add("Anima", c, 4)
# Hyperconductive: 5 Aether Oil (350 Poetics each).
hw_add("Hyperconductive", "Aether Oil", 5)
# Reconditioned: ~57-60 each of Umbrite + Crystal Sand (use 60).
hw_add("Reconditioned", "Umbrite", 60)
hw_add("Reconditioned", "Crystal Sand", 60)
# Sharpened: 50 Singing Cluster.
hw_add("Sharpened", "Singing Cluster", 50)
# Complete: 15 Pneumite (+ aetheric density grind -> note).
hw_add("Complete", "Pneumite", 15)
# Lux: 1 Archaic Enchanted Ink (+ 12 Hard trials -> note).
hw_add("Lux", "Archaic Enchanted Ink", 1)

# ===== SB Eureka weapons — part 1: Base..Eureka (Physeos = part 2) =====
# Catalog tracks all 15 sub-tiers; Wyn lumped totals at the major milestones. Place each sub-tier's
# exact crystal cost. Real singular item names (zones via material_sources). 15 jobs (flags ignored).
sb_rows=[]
def sb_add(step, material, per):
    sb_rows.append({"step":step,"material":material,"perUnit":per,"jobs":[True]*15})
# Eureka Anemos — Protean Crystal (1300 total) + 3 Pazuzu's Feather.
sb_add("Base", "Protean Crystal", 100)
sb_add("Base +1", "Protean Crystal", 400)
sb_add("Base +2", "Protean Crystal", 800)
sb_add("Anemos", "Pazuzu's Feather", 3)
# Eureka Pagos — Frosted Protean Crystal (31 total) + 500 Pagos Crystal + 5 Louhi's Ice.
sb_add("Pagos", "Frosted Protean Crystal", 5)
sb_add("Pagos +1", "Frosted Protean Crystal", 10)
sb_add("Pagos +1", "Pagos Crystal", 500)
sb_add("Elemental", "Frosted Protean Crystal", 16)
sb_add("Elemental", "Louhi's Ice", 5)
# Eureka Pyros — Pyros Crystal (650 total) + 5 Penthesilea's Flame (+ Logos Actions note).
sb_add("Elemental +1", "Pyros Crystal", 150)
sb_add("Elemental +2", "Pyros Crystal", 200)
sb_add("Pyros", "Pyros Crystal", 300)
sb_add("Pyros", "Penthesilea's Flame", 5)
# Eureka Hydatos — Hydatos Crystal (350 total) + 5 Crystalline Scale.
sb_add("Hydatos", "Hydatos Crystal", 50)
sb_add("Hydatos +1", "Hydatos Crystal", 100)
sb_add("Base (Physeos)", "Hydatos Crystal", 100)
sb_add("Eureka", "Hydatos Crystal", 100)
sb_add("Eureka", "Crystalline Scale", 5)
# Physeos — 100 Eureka Fragment, dropped only in The Baldesion Arsenal (~28/clear, ~4 runs).
sb_add("Physeos", "Eureka Fragment", 100)
SB_STEPS=["Base","Base +1","Base +2","Anemos","Pagos","Pagos +1","Elemental",
          "Elemental +1","Elemental +2","Pyros","Hydatos","Hydatos +1","Base (Physeos)","Eureka","Physeos"]

# ===== ShB Resistance weapons (Bozja) — part 1: Resistance / Augmented / Recollection / Law's Order =====
# Memory-grind relic; Wyn's data was correct here, just moved to the supplement with real singular
# names (memory items). 17 jobs (flags ignored). Augmented Law's Order + Blade's = part 2.
shb_rows=[]
def shb_add(step, material, per):
    shb_rows.append({"step":step,"material":material,"perUnit":per,"jobs":[True]*17})
shb_add("Resistance", "Thavnairian Scalepowder", 4)          # subsequent weapons (first is free)
for m in ["Tortured","Sorrowful","Harrowing"]:
    shb_add("Augmented", f"{m} Memory of the Dying", 20)
shb_add("Recollection", "Bitter Memory of the Dying", 6)
shb_add("Law's Order", "Loathsome Memory of the Dying", 15)
shb_add("Augmented Law's Order", "Timeworn Artifact", 15)   # 3/Delubrum Reginae clear, or PotD
shb_add("Blade's", "Raw Emotion", 15)                       # Dalriada 3 / DR 2 / SB dungeon 1 / HoH
# One-time grinds (1st: Haunting/Vexatious; 2nd: Zadnor Compact Axle etc.) + optional Aetherial
# Sealant reallocation are one-time / optional, NOT per weapon -> notes only.
# replaceSteps uses Wyn's step name "Augmented Law's" (catalog step is "Augmented Law's Order").
SHB_STEPS=["Resistance","Augmented","Recollection","Law's Order","Augmented Law's","Blade's","Bozjan Haste"]

# ===== EW Manderville weapons — each tier = 3 of a Poetics item (500 each) from Jubrunnah. =====
# Wyn's data was correct; moved to the supplement for consistency. 19 jobs (flags ignored).
ew_rows=[]
def ew_add(step, material):
    ew_rows.append({"step":step,"material":material,"perUnit":3,"jobs":[True]*19})
ew_add("Manderville", "Manderium Meteorite")
ew_add("Amazing", "Complementary Chondrite")
ew_add("Majestic", "Amplifying Achondrite")
ew_add("Mandervillous", "Cosmic Crystallite")
EW_STEPS=["Manderville","Amazing","Majestic","Mandervillous"]

# ===== DT Phantom weapons (Occult Crescent) — 3 tiers, each 3 of an "Arcanite" per weapon. =====
# Per weapon: exchange 1,500 Allagan Tomestones of Mathematics for 3 arcanites (Ermina, Phantom
# Village) + the previous tier's weapon, traded to Dodokkuli. Each tier has a one-time unlock grind
# (demiatmas / 5 items + aether array / 4 items + crystal paste) -> notes. 21 jobs (flags ignored).
# Wyn's weapon rows were correct; "Step 4"/"Arcanaut" Wyn rows are leaked future/armor data -> dropped.
dt_rows=[]
def dt_add(step, material):
    dt_rows.append({"step":step,"material":material,"perUnit":3,"jobs":[True]*21})
dt_add("Penumbrae", "Arcanite")
dt_add("Umbrae", "Waxing Arcanite")
dt_add("Obscurum", "Waning Arcanite")

# The curated JSON is now the sole source of relic materials;
# RelicDataService loads each expansion's rows wholesale, so no replaceSteps is needed.
data={
 "DoHDoL":rows,
 "ARR":arr_rows,
 "HW":hw_rows,
 "SB":sb_rows,
 "ShB":shb_rows,
 "EW":ew_rows,
 "DT":dt_rows,
}
out=os.path.join(os.path.dirname(__file__),"extracted","tool_extra_materials.json")
json.dump(data,open(out,"w",encoding="utf-8"),ensure_ascii=False,indent=2)
print(f"DoHDoL {len(rows)} · ARR {len(arr_rows)} · HW {len(hw_rows)} · SB {len(sb_rows)} · ShB {len(shb_rows)}")
