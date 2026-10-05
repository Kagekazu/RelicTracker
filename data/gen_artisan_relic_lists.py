"""Emit Artisan RelicToolPremadeLists definitions from gen_tool_materials.py.

Prints the C# file to stdout, and also writes it into a sibling Artisan checkout if one exists.
"""
import csv
import json
import re
import sys
from pathlib import Path

from item_lookup import load_item_index

text = Path(__file__).with_name("gen_tool_materials.py").read_text(encoding="utf-8")
C = ["CRP", "BSM", "ARM", "GSM", "LTW", "WVR", "ALC", "CUL"]

# In Artisan's RelicToolStep enum order; the definitions are written in this order.
STEP_ENUM = {
    "Skysteel +1": "SkysteelPlus1",
    "Dragonsung": "Dragonsung",
    "Augmented Dragonsung": "AugmentedDragonsung",
    "Skysung": "Skysung",
    "Skybuilders'": "Skybuilders",
    "Augmented": "Augmented",
    "Crystalline": "Crystalline",
    "Chora-Zoi's": "ChoraZois",
    "Brilliant": "Brilliant",
    "Vrandtic": "Vrandtic",
    "Lodestar": "Lodestar",
    "Supra": "Supra",
    "Lucis": "Lucis",
    "Resplendent A": "ResplendentA",
    "Resplendent B": "ResplendentB",
    "Resplendent C": "ResplendentC",
}

# Resplendent crafts a component from each material tier, with a turn-in between tiers, so each
# tier is its own list. Best case (max collectability) is 30 of each component per class.
CLASS_NAME = ["Carpenter", "Blacksmith", "Armorer", "Goldsmith", "Leatherworker", "Weaver", "Alchemist", "Culinarian"]
RESPLENDENT_PER_TIER = 30

STEP_ORDER = list(STEP_ENUM.keys())


def extract_brace_block(s, start):
    depth = 0
    for i in range(start, len(s)):
        if s[i] == "{":
            depth += 1
        elif s[i] == "}":
            depth -= 1
            if depth == 0:
                return s[start : i + 1]
    raise ValueError("unbalanced braces")


def parse_crafter(step, cnt, block):
    out = []
    for cls in C:
        pat = rf'"{cls}":\(".*?",\[.*?\],"([^"]+)"\)'
        m = re.search(pat, block, re.S)
        if m:
            out.append((step, cls, m.group(1), cnt))
    return out


def parse_craft_tree(step, cnt, block):
    out = []
    for cls in C:
        pat = rf'"{cls}":\s*\{{[^}}]*"collectable":\s*"([^"]+)"'
        m = re.search(pat, block)
        if m:
            out.append((step, cls, m.group(1), cnt))
    return out


entries = []
for m in re.finditer(r'crafter\("([^"]+)",(\d+),\d+,\{', text):
    step, cnt = m.group(1), int(m.group(2))
    block = extract_brace_block(text, m.end() - 1)
    entries.extend(parse_crafter(step, cnt, block))

for m in re.finditer(r'craft_tree\("([^"]+)",\s*(\d+),\s*\{', text):
    step, cnt = m.group(1), int(m.group(2))
    block = extract_brace_block(text, m.end() - 1)
    entries.extend(parse_craft_tree(step, cnt, block))

for tier in "ABC":
    for cls, name in zip(C, CLASS_NAME):
        entries.append((f"Resplendent {tier}", cls, f"Resplendent {name}'s Component {tier}", RESPLENDENT_PER_TIER))

# Artisan definitions use recipe IDs: (item, crafter class) -> recipe, from the game's Recipe sheet
# (cached by obtain_lookup in extracted/_sheets).
recipes_by_item = {}
with open(Path(__file__).with_name("extracted") / "_sheets" / "Recipe.csv", encoding="utf-8-sig") as handle:
    reader = csv.reader(handle)
    header = next(reader)
    item_col, craft_col = header.index("ItemResult"), header.index("CraftType")
    for row in reader:
        if row and row[0].isdigit() and row[item_col] not in ("", "0"):
            recipes_by_item.setdefault(int(row[item_col]), []).append((int(row[craft_col]), int(row[0])))


def recipe_id(item_id, craft_type=None):
    """Recipe for an item, for one crafter class or (craft_type None) the first class that has one."""
    for recipe_craft_type, rid in sorted(recipes_by_item.get(item_id, [])):
        if craft_type is None or recipe_craft_type == craft_type:
            return rid
    raise SystemExit(f"No recipe for item {item_id} (craft type {craft_type})")


index = load_item_index(Path(__file__).with_name("extracted") / "_item.csv")

lines = [
    "namespace Artisan.CraftingLists;",
    "",
    "internal static partial class RelicToolPremadeLists",
    "{",
    "    // (step, quantity, recipeId) - the recipe sets the crafter class; the list ID is derived.",
    "    private static readonly RelicToolPremadeEntry[] Definitions =",
    "    [",
]

entries.sort(key=lambda entry: STEP_ORDER.index(entry[0]) if entry[0] in STEP_ORDER else len(STEP_ORDER))
last_step = None
for step, cls, coll, cnt in entries:
    if step not in STEP_ENUM:
        raise SystemExit(f"Unknown Artisan relic step: {step!r}")
    if step != last_step:
        lines.append(f"        // {step}")
        last_step = step
    item_id = index.resolve(coll)
    enum_name = STEP_ENUM[step]
    craft_type = C.index(cls)
    lines.append(f"        new(RelicToolStep.{enum_name}, {cnt}, {recipe_id(item_id, craft_type)}),")

lines.extend(
    [
        "    ];",
    ]
)

# Weapon steps, read from the generated material data. Crafts every job needs become one list per
# step across all crafter classes (Artisan's AllClassesSlot); crafts only one job needs (the
# Zodiac base weapons) become one list per relic job.
WEAPON_STEP_ENUM = {
    ("ARR", "Relic"): "Relic",
    ("ARR", "Zodiac"): "Zodiac",
    ("HW", "Anima"): "Anima",
    ("DT", "Umbrae"): "Umbrae",
    ("DT", "Obscurum"): "Obscurum",
    ("DT", "Eclipticum"): "Eclipticum",
}
extracted = Path(__file__).with_name("extracted")
materials = json.loads((extracted / "tool_extra_materials.json").read_text(encoding="utf-8"))
relic_lines = json.loads((extracted / "relic_lines.json").read_text(encoding="utf-8"))
relic_lines = relic_lines if isinstance(relic_lines, list) else relic_lines.get("lines", list(relic_lines.values()))
job_list_by_expansion = {line["expansion"]: line["jobList"] for line in relic_lines if line["expansion"] != "DoHDoL"}
weapon_lists = {}
job_lists = []
for expansion, rows in materials.items():
    if expansion == "DoHDoL":
        continue
    for row in rows:
        if (row.get("obtain") != "craft" and not row.get("craftable")) or row.get("role") in ("quest", "covers"):
            continue
        key = (expansion, row["step"])
        if key not in WEAPON_STEP_ENUM:
            raise SystemExit(f"Crafted weapon material with no Artisan step: {key} {row['material']!r}")
        if all(row["jobs"]):
            weapon_lists.setdefault(key, []).append((row["materialIds"][0], row["perUnit"], row["material"]))
        else:
            for slot, flagged in enumerate(row["jobs"]):
                if flagged:
                    job = job_list_by_expansion[expansion][slot]
                    job_lists.append((key, slot, job, row["perUnit"], row["materialIds"][0], row["material"]))

lines.extend(
    [
        "",
        "    // (step, (recipeId, quantity) per weapon) - one list across all crafter classes.",
        "    private static readonly RelicWeaponPremadeEntry[] WeaponDefinitions =",
        "    [",
    ]
)
for key, items in weapon_lists.items():
    names = ", ".join(name for _, _, name in items)
    pairs = ", ".join(f"({recipe_id(item_id)}u, {qty})" for item_id, qty, _ in items)
    lines.append(f"        // {key[1]}: {names}")
    lines.append(f"        new(RelicToolStep.{WEAPON_STEP_ENUM[key]}, [{pairs}]),")
lines.extend(
    [
        "    ];",
        "",
        "    // (step, relic job slot, job, quantity, recipeId) - one list per relic job.",
        "    private static readonly RelicJobPremadeEntry[] JobDefinitions =",
        "    [",
    ]
)
last_key = None
for key, slot, job, qty, item_id, name in sorted(job_lists, key=lambda entry: (entry[0], entry[1])):
    if key != last_key:
        lines.append(f"        // {key[1]}")
        last_key = key
    lines.append(f"        new(RelicToolStep.{WEAPON_STEP_ENUM[key]}, {slot}, \"{job}\", {qty}, {recipe_id(item_id)}), // {name}")
lines.extend(["    ];", "}"])
body = "\n".join(lines) + "\n"
print(body)
out_path = Path(__file__).resolve().parents[2] / "Artisan" / "Artisan" / "CraftingList" / "RelicToolPremadeLists.Definitions.cs"
if out_path.parent.exists():
    out_path.write_text(body, encoding="utf-8", newline="\n")  # Artisan uses LF
    print(f"Wrote {out_path}", file=sys.stderr)
