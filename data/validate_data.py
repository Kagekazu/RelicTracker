#!/usr/bin/env python3
"""Validate RelicTracker's bundled data files."""

from __future__ import annotations

import argparse
import csv
import json
import sys
import xml.etree.ElementTree as ET
from pathlib import Path
from typing import Iterable


ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT / "data" / "extracted"
PROJECT = ROOT / "RelicTracker" / "RelicTracker.csproj"
LEGACY_LIVE_FILES = ("expansions.json", "notes.json")


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
        if not include or not include.startswith("..\\data\\extracted\\"):
            continue
        # csproj paths use Windows separators; normalize so this also runs on Linux CI.
        files.append((PROJECT.parent / include.replace("\\", "/")).resolve())
    return files


def iter_material_rows(extra: dict[str, list[dict]]) -> Iterable[tuple[str, int, dict]]:
    for expansion, rows in extra.items():
        for index, row in enumerate(rows):
            yield expansion, index, row


def load_item_names(path: Path | None) -> tuple[set[str], str | None]:
    if path is None or not path.exists():
        return set(), None

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


def expand_name_variants(name: str) -> Iterable[str]:
    yield name
    if name.lower().startswith("hq "):
        yield name[3:].strip()
    if name.endswith(" Parts"):
        yield name[:-1]
        yield name.replace(" Parts", " Component")
        yield name.replace(" Parts", " Components")
    if name.endswith(" Pars"):
        yield name.replace(" Pars", " Part")
        yield name.replace(" Pars", " Parts")
    if name.endswith(" parts"):
        yield name[:-1]


def alias_targets(value) -> list[str]:
    if isinstance(value, str):
        return [value]
    if isinstance(value, list):
        return [item for item in value if isinstance(item, str)]
    return []


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

    for filename in LEGACY_LIVE_FILES:
        if (DATA / filename).exists():
            fail(errors, f"Legacy Wyn output should not live in data/extracted: {filename}")

    manifest = load_json(DATA / "manifest.json")
    relic_lines = load_json(DATA / "relic_lines.json")
    relic_armor = load_json(DATA / "relic_armor.json")
    extra = load_json(DATA / "tool_extra_materials.json")
    sources = load_json(DATA / "material_sources.json")
    aliases = load_json(DATA / "material_aliases.json")
    armor_costs = load_json(DATA / "armor_costs.json")

    expansions = set(manifest.get("expansions") or [])

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
                piece_names = tier.get("pieceNames") or []
                if pieces <= 0:
                    fail(errors, f"relic_armor[{index}] set[{set_index}] tier[{tier_index}] has invalid pieces")
                if len(piece_names) != pieces:
                    fail(
                        errors,
                        f"relic_armor[{index}] set[{set_index}] tier[{tier_index}] pieceNames count does not match pieces",
                    )
                if any(not isinstance(name, str) or not name.strip() for name in piece_names):
                    fail(
                        errors,
                        f"relic_armor[{index}] set[{set_index}] tier[{tier_index}] has blank pieceNames entries",
                    )

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

        material_ids = row.get("materialIds") or []
        if not isinstance(material_ids, list) or not material_ids:
            fail(errors, f"{expansion}[{index}] has no materialIds")
        elif any(not isinstance(item_id, int) or item_id <= 0 for item_id in material_ids):
            fail(errors, f"{expansion}[{index}] has invalid materialIds entries")

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
            currency_id = cost.get("currencyId")
            currency_ids = cost.get("currencyIds") or []
            if isinstance(currency_id, int) and currency_id > 0 and not currency_ids:
                currency_ids = [currency_id]
            if not isinstance(currency_ids, list) or not currency_ids:
                fail(errors, f"armor_costs {expansion}[{index}] has no currencyIds")
            elif any(not isinstance(item_id, int) or item_id <= 0 for item_id in currency_ids):
                fail(errors, f"armor_costs {expansion}[{index}] has invalid currencyIds entries")

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
