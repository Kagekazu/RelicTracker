#!/usr/bin/env python3
"""Validate RelicTracker's bundled data files."""

from __future__ import annotations

import argparse
import csv
import json
import math
import sys
import xml.etree.ElementTree as ET
from pathlib import Path
from typing import Iterable

sys.path.insert(0, str(Path(__file__).resolve().parent))
from item_lookup import alias_targets, expand_name_variants
from obtain_lookup import KINDS as OBTAIN_KINDS


ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT / "data" / "extracted"
PROJECT = ROOT / "RelicTracker" / "RelicTracker.csproj"


def load_json(path: Path):
    with path.open("r", encoding="utf-8") as handle:
        return json.load(handle)


def fail(errors: list[str], message: str) -> None:
    errors.append(message)


def bundled_data_files() -> list[Path]:
    project = ET.parse(PROJECT)
    files: list[Path] = []
    for node in project.findall(".//None"):
        include = node.attrib.get("Include")
        if not include or not include.startswith("..\\data\\") or not include.endswith(".json"):
            continue
        # csproj paths use Windows separators; normalize so this also runs on Linux.
        files.append((PROJECT.parent / include.replace("\\", "/")).resolve())
    return files


MATERIAL_ROLES = {"craft", "precraft", "scrip", "quest", "covers"}
SLOTS = ["Head", "Body", "Hands", "Legs", "Feet"]

# Mirror of ArmorCostCalculator.CostLinks: armor_costs set -> (armor set name, tier label, slot).
ARMOR_COST_LINKS = {
    "Artifact (base)": ("Eurekan", "Base", None),
    "Artifact +1": ("Eurekan", "+1", None),
    "Artifact +2": ("Eurekan", "+2", None),
    "Anemos (final)": ("Eurekan", "Anemos", None),
    "Elemental (base)": ("Elemental", "Base", None),
    "Elemental +1": ("Elemental", "+1", None),
    "Elemental +2": ("Elemental", "+2", None),
    "Bozjan": ("Bozjan", "Base", None),
    **{f"Aug. Bozjan ({s})": ("Bozjan", "Augmented", i) for i, s in enumerate(SLOTS)},
    "Law's Order": ("Law's Order", "Base", None),
    **{f"Aug. Law's Order ({s})": ("Law's Order", "Augmented", i) for i, s in enumerate(SLOTS)},
    "Blade's": ("Blade's", "Base", None),
    "Arcanaut's (base)": ("Arcanaut's", "Base", None),
    "Arcanaut's +1": ("Arcanaut's", "+1", None),
    "Arcanaut's +2": ("Arcanaut's", "+2", None),
    "Phantom Vision (base)": ("Phantom Vision", "Base", None),
    "Phantom Vision +1": ("Phantom Vision", "+1", None),
    "Phantom Vision +2": ("Phantom Vision", "+2", None),
    "Phantom Vision +3": ("Phantom Vision", "+3", None),
}


def check_recipes(extra: dict[str, list[dict]], recipe_csv: Path, warnings: list[str]) -> list[str]:
    """Each precraft's raw rows must match the game recipe, expanding intermediate crafts the data skips."""
    if not recipe_csv.exists():
        warnings.append(f"Skipped recipe check: {recipe_csv} not found.")
        return []

    with recipe_csv.open("r", encoding="utf-8", errors="replace", newline="") as handle:
        table = list(csv.reader(handle))
    col = {name: i for i, name in enumerate(table[0])}
    recipes: dict[int, list[tuple[int, int, dict[int, int]]]] = {}
    for record in table[1:]:
        if not record or not record[0].isdigit() or int(record[col["ItemResult"]] or 0) <= 0:
            continue
        ingredients: dict[int, int] = {}
        for k in range(8):
            item_id = int(record[col[f"Ingredient[{k}]"]] or 0)
            amount = int(record[col[f"AmountIngredient[{k}]"]] or 0)
            if item_id > 19 and amount > 0:  # ids 2-19 are crystals, which the data leaves out
                ingredients[item_id] = ingredients.get(item_id, 0) + amount
        recipes.setdefault(int(record[col["ItemResult"]]), []).append(
            (int(record[col["CraftType"]] or 0), int(record[col["AmountResult"]] or 1), ingredients))

    errors: list[str] = []
    for expansion, rows in extra.items():
        for product in rows:
            ids = product.get("materialIds") or []
            if product.get("role") != "precraft" or len(ids) != 1:
                continue
            slots = [i for i, flag in enumerate(product.get("jobs") or []) if flag and i < 8]
            candidates = [r for r in recipes.get(ids[0], []) if r[0] in slots] or recipes.get(ids[0], [])
            if not candidates:
                continue
            actual: dict[int, int] = {}
            for row in rows:
                row_ids = row.get("materialIds") or []
                if (row.get("craftOf") == product.get("material") and row.get("step") == product.get("step")
                        and row.get("jobs") == product.get("jobs") and len(row_ids) == 1):
                    actual[row_ids[0]] = actual.get(row_ids[0], 0) + int(row.get("perUnit") or 0)
            if not actual:
                continue

            expected: dict[int, int] = {}

            def expand(item_id: int, quantity: int, depth: int) -> None:
                sub = recipes.get(item_id)
                if item_id in actual or not sub or depth > 3:
                    expected[item_id] = expected.get(item_id, 0) + quantity
                    return
                _, result_yield, sub_ingredients = sub[0]
                crafts = math.ceil(quantity / result_yield)
                for sub_id, amount in sub_ingredients.items():
                    expand(sub_id, amount * crafts, depth + 1)

            _, product_yield, ingredients = candidates[0]
            crafts = math.ceil(int(product.get("perUnit") or 0) / product_yield)
            for item_id, amount in ingredients.items():
                expand(item_id, amount * crafts, 0)
            if actual != expected:
                errors.append(
                    f"{expansion} {product.get('step')} {product.get('material')}: raw materials {actual} "
                    f"do not match the game recipe {expected}")
    return errors


def iter_material_rows(extra: dict[str, list[dict]]) -> Iterable[tuple[str, int, dict]]:
    for expansion, rows in extra.items():
        for index, row in enumerate(rows):
            yield expansion, index, row


def load_item_names(path: Path | None) -> tuple[set[str], str | None]:
    if path is None or not path.exists():
        return set(), f"Skipped item resolution: {path} not found."

    first = path.read_text(encoding="utf-8", errors="replace")[:128]
    if first.startswith("404:") or "," not in first:
        return set(), f"Skipped item resolution: {path} is not a valid CSV export."

    names: set[str] = set()
    with path.open("r", encoding="utf-8", newline="") as handle:
        reader = csv.DictReader(handle)
        field = "Name" if "Name" in (reader.fieldnames or []) else None
        if field is None:
            field = "name" if "name" in (reader.fieldnames or []) else None
        if field is None:
            return set(), f"Skipped item resolution: {path} has no Name column."

        for row in reader:
            name = (row.get(field) or "").strip()
            if name:
                names.add(name.casefold())

    return names, None


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--item-csv",
        type=Path,
        default=DATA / "_item.csv",
        help="Optional item-name CSV used to validate material and alias resolution.",
    )
    args = parser.parse_args()

    errors: list[str] = []
    warnings: list[str] = []

    for path in bundled_data_files():
        if not path.exists():
            fail(errors, f"Bundled data file is missing: {path.relative_to(ROOT)}")
            continue
        try:
            load_json(path)
        except json.JSONDecodeError as ex:
            fail(errors, f"Bundled data file is invalid JSON: {path.relative_to(ROOT)} ({ex})")

    manifest = load_json(DATA / "manifest.json")
    relic_lines = load_json(DATA / "relic_lines.json")
    relic_armor = load_json(DATA / "relic_armor.json")
    extra = load_json(DATA / "tool_extra_materials.json")
    sources = load_json(DATA / "material_sources.json")
    aliases = load_json(DATA / "material_aliases.json")
    armor_costs = load_json(DATA / "armor_costs.json")

    expansions = set(manifest.get("expansions") or [])
    armor_tier_pieces = {
        (line.get("expansion"), armor_set.get("name"), key): int(tier.get("pieces") or 0)
        for line in relic_armor
        for armor_set in line.get("sets") or []
        for tier in armor_set.get("tiers") or []
        for key in (tier.get("label"), tier.get("collectType"))
    }
    jobs_by_expansion = {line.get("expansion"): int(line.get("jobs") or 0) for line in relic_lines}

    for index, line in enumerate(relic_lines):
        expansion = line.get("expansion")
        jobs = int(line.get("jobs") or 0)
        tier_count = int(line.get("tierCount") or 0)
        relic_count = int(line.get("relicCount") or 0)
        steps = line.get("steps") or []
        relic_names = line.get("relicNames") or []

        if expansion not in expansions:
            fail(errors, f"relic_lines[{index}] uses expansion not in manifest: {expansion}")
        if jobs <= 0 or tier_count <= 0:
            fail(errors, f"relic_lines[{index}] has invalid jobs/tierCount")
        if len(steps) != tier_count:
            fail(errors, f"relic_lines[{index}] step count does not match tierCount")
        if relic_count != jobs * tier_count:
            fail(errors, f"relic_lines[{index}] relicCount does not equal jobs * tierCount")
        if len(relic_names) != relic_count:
            fail(errors, f"relic_lines[{index}] relicNames count does not match relicCount")
        if any(not isinstance(name, str) or not name.strip() for name in relic_names):
            fail(errors, f"relic_lines[{index}] has blank relicNames entries")

        job_list = line.get("jobList") or []
        slot_relics = line.get("slotRelics") or []
        if len(job_list) != jobs:
            fail(errors, f"relic_lines[{index}] jobList length does not match jobs")
        if len(slot_relics) != jobs:
            fail(errors, f"relic_lines[{index}] slotRelics length does not match jobs")

        relic_ids = line.get("relicIds") or []
        slot_relic_ids = line.get("slotRelicIds") or []
        relic_replica_ids = line.get("relicReplicaIds") or []
        if len(relic_ids) != relic_count:
            fail(errors, f"relic_lines[{index}] relicIds count does not match relicCount")
        if len(slot_relic_ids) != jobs:
            fail(errors, f"relic_lines[{index}] slotRelicIds length does not match jobs")
        if len(relic_replica_ids) != relic_count:
            fail(errors, f"relic_lines[{index}] relicReplicaIds count does not match relicCount")
        if any(not isinstance(item_id, int) or item_id <= 0 for item_id in relic_ids):
            fail(errors, f"relic_lines[{index}] has invalid relicIds entries")
        if any(not isinstance(item_id, int) or item_id <= 0 for item_id in slot_relic_ids):
            fail(errors, f"relic_lines[{index}] has invalid slotRelicIds entries")
        for replica_index, replicas in enumerate(relic_replica_ids):
            if not isinstance(replicas, list):
                fail(errors, f"relic_lines[{index}] relicReplicaIds[{replica_index}] is not a list")
                continue
            if any(not isinstance(item_id, int) or item_id <= 0 for item_id in replicas):
                fail(errors, f"relic_lines[{index}] relicReplicaIds[{replica_index}] has invalid ids")

    for index, line in enumerate(relic_armor):
        expansion = line.get("expansion")
        if expansion not in expansions:
            fail(errors, f"relic_armor[{index}] uses expansion not in manifest: {expansion}")
        for set_index, armor_set in enumerate(line.get("sets") or []):
            for tier_index, tier in enumerate(armor_set.get("tiers") or []):
                pieces = int(tier.get("pieces") or 0)
                if pieces <= 0:
                    fail(errors, f"relic_armor[{index}] set[{set_index}] tier[{tier_index}] has invalid pieces")

                piece_ids = tier.get("pieceIds") or []
                if len(piece_ids) != pieces:
                    fail(
                        errors,
                        f"relic_armor[{index}] set[{set_index}] tier[{tier_index}] pieceIds count does not match pieces",
                    )
                if any(not isinstance(item_id, int) or item_id <= 0 for item_id in piece_ids):
                    fail(
                        errors,
                        f"relic_armor[{index}] set[{set_index}] tier[{tier_index}] has invalid pieceIds entries",
                    )
                if "pieceNames" in tier:
                    fail(
                        errors,
                        f"relic_armor[{index}] set[{set_index}] tier[{tier_index}] still has pieceNames (IDs only)",
                    )

    products = {
        (expansion, (row.get("step") or "").strip().casefold(), (row.get("material") or "").strip().casefold())
        for expansion, _, row in iter_material_rows(extra)
    }
    material_names: set[str] = set()
    for expansion, index, row in iter_material_rows(extra):
        if expansion not in expansions:
            fail(errors, f"Material row uses expansion not in manifest: {expansion}")

        step = (row.get("step") or "").strip()
        material = (row.get("material") or "").strip()
        per_unit = row.get("perUnit")
        jobs = row.get("jobs")
        role = (row.get("role") or "").strip().lower()

        if not step:
            fail(errors, f"{expansion}[{index}] has no step")
        if not material:
            fail(errors, f"{expansion}[{index}] has no material")
        elif role != "covers":
            material_names.add(material)
        if role == "quest":
            if not isinstance(per_unit, (int, float)) or per_unit < 0:
                fail(errors, f"{expansion}[{index}] has invalid perUnit: {per_unit!r}")
        elif not isinstance(per_unit, (int, float)) or per_unit <= 0:
            fail(errors, f"{expansion}[{index}] has invalid perUnit: {per_unit!r}")
        if not isinstance(jobs, list) or not jobs or not all(isinstance(flag, bool) for flag in jobs):
            fail(errors, f"{expansion}[{index}] has invalid jobs flags")
        if role not in ("quest", "covers") and row.get("obtain") not in OBTAIN_KINDS:
            fail(errors, f"{expansion}[{index}] {material} has invalid obtain: {row.get('obtain')!r}")
        if role and role not in MATERIAL_ROLES:
            fail(errors, f"{expansion}[{index}] has unknown role: {role!r}")
        if isinstance(per_unit, float) and not per_unit.is_integer():
            fail(errors, f"{expansion}[{index}] perUnit {per_unit} is not a whole number (the plugin rounds it)")
        if isinstance(jobs, list) and jobs_by_expansion.get(expansion) and len(jobs) != jobs_by_expansion[expansion]:
            fail(errors, f"{expansion}[{index}] has {len(jobs)} job flags, its relic line has {jobs_by_expansion[expansion]} jobs")
        craft_of = (row.get("craftOf") or "").strip()
        if craft_of and role != "covers" and (expansion, step.casefold(), craft_of.casefold()) not in products:
            fail(errors, f"{expansion}[{index}] craftOf {craft_of!r} is not a material in step {step!r}")
        if "oneTime" in row and not isinstance(row["oneTime"], bool):
            fail(errors, f"{expansion}[{index}] has non-boolean oneTime")

        material_ids = row.get("materialIds") or []
        if not isinstance(material_ids, list) or not material_ids:
            fail(errors, f"{expansion}[{index}] has no materialIds")
        elif any(not isinstance(item_id, int) or item_id <= 0 for item_id in material_ids):
            fail(errors, f"{expansion}[{index}] has invalid materialIds entries")

    # The plugin silently skips rows, checklists and notes whose step or series name has no match.
    steps_by_expansion: dict[str, set[str]] = {}
    steps_by_series: dict[str, set[str]] = {}
    for line in relic_lines:
        steps = {step.strip().casefold() for step in line.get("steps") or []}
        steps_by_expansion.setdefault(line.get("expansion"), set()).update(steps)
        steps_by_series[line.get("collectType")] = {step.strip() for step in line.get("steps") or []}
    for line in relic_armor:
        steps_by_series[line.get("lineName")] = {
            tier.get("collectType") for armor_set in line.get("sets") or [] for tier in armor_set.get("tiers") or []
        }

    for expansion, index, row in iter_material_rows(extra):
        step = (row.get("step") or "").strip()
        if step and step.casefold() not in steps_by_expansion.get(expansion, set()):
            fail(errors, f"{expansion}[{index}] step {step!r} is not a relic step in that expansion")

    for file_name, entries, allowed_extra in (
        ("relic_checklists.json", load_json(ROOT / "data" / "relic_checklists.json"), set()),
        ("relic_step_notes.json", load_json(DATA / "relic_step_notes.json"), {"_line"}),
    ):
        for series, by_step in entries.items():
            if series.startswith("_"):
                continue
            if series not in steps_by_series:
                fail(errors, f"{file_name} series is not a relic or armor line: {series}")
                continue
            for step, text in by_step.items():
                if step not in steps_by_series[series] and step not in allowed_extra:
                    fail(errors, f"{file_name} {series} has unknown step: {step}")
                if not isinstance(text, str):
                    fail(errors, f"{file_name} {series}/{step} is not text")

    source_keys = {key for key in sources if not key.startswith("_")}
    stale_sources = sorted(source_keys - material_names)
    for key in stale_sources:
        fail(errors, f"material_sources.json key is not a tracked material: {key}")

    for alias, value in aliases.items():
        targets = alias_targets(value)
        if not targets:
            fail(errors, f"material_aliases.json has invalid target for {alias!r}")
        if alias not in material_names:
            warnings.append(f"Alias key is not currently used by tracked materials: {alias}")

    for expansion, costs in armor_costs.items():
        if expansion not in expansions:
            fail(errors, f"Armor costs use expansion not in manifest: {expansion}")
        for index, cost in enumerate(costs):
            if not (cost.get("set") or "").strip():
                fail(errors, f"armor_costs {expansion}[{index}] has no set")
            if not (cost.get("currency") or "").strip():
                fail(errors, f"armor_costs {expansion}[{index}] has no currency")
            if int(cost.get("allTotal") or 0) < 0:
                fail(errors, f"armor_costs {expansion}[{index}] has negative allTotal")
            per_piece = int(cost.get("perPiece") or 0)
            set_total = int(cost.get("setTotal") or 0)
            single_slot = set_total == per_piece
            if not single_slot and set_total != per_piece * 5 and (set_total - 2 * per_piece) % 3 != 0:
                fail(errors, f"armor_costs {expansion}[{index}] setTotal does not split into 2 body/legs + 3 other pieces")
            if not single_slot and set_total - 2 * per_piece < 0:
                fail(errors, f"armor_costs {expansion}[{index}] setTotal is below the body/legs cost")
            currency_ids = cost.get("currencyIds") or []
            link = ARMOR_COST_LINKS.get((cost.get("set") or "").strip())
            if link is None:
                fail(errors, f"armor_costs {expansion}[{index}] set {cost.get('set')!r} is not in ArmorCostCalculator.CostLinks")
            else:
                pieces = armor_tier_pieces.get((expansion, link[0], link[1]))
                if pieces is None:
                    fail(errors, f"armor_costs {expansion}[{index}] links to a missing armour tier {link[:2]}")
                elif int(cost.get("allTotal") or 0) != int(cost.get("setTotal") or 0) * (pieces // 5):
                    fail(errors, f"armor_costs {expansion}[{index}] allTotal is not setTotal x {pieces // 5} sets")
            if not isinstance(currency_ids, list) or not currency_ids:
                fail(errors, f"armor_costs {expansion}[{index}] has no currencyIds")
            elif any(not isinstance(item_id, int) or item_id <= 0 for item_id in currency_ids):
                fail(errors, f"armor_costs {expansion}[{index}] has invalid currencyIds entries")

    errors.extend(check_recipes(extra, DATA / "_sheets" / "Recipe.csv", warnings))

    item_names, item_warning = load_item_names(args.item_csv)
    if item_warning:
        warnings.append(item_warning)
    if item_names:
        for material in sorted(material_names):
            targets = alias_targets(aliases.get(material, material))
            candidates = [candidate for target in targets for candidate in expand_name_variants(target)]
            if not any(candidate.casefold() in item_names for candidate in candidates):
                fail(errors, f"Material does not resolve against item CSV: {material}")

    for warning in warnings:
        print(f"warning: {warning}")

    if errors:
        for error in errors:
            print(f"error: {error}", file=sys.stderr)
        return 1

    print("Data validation passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
