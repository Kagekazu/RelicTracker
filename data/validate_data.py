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
        files.append((PROJECT.parent / include).resolve())
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
    extra = load_json(DATA / "tool_extra_materials.json")
    sources = load_json(DATA / "material_sources.json")
    aliases = load_json(DATA / "material_aliases.json")
    armor_costs = load_json(DATA / "armor_costs.json")

    expansions = set(manifest.get("expansions") or [])
    material_names: set[str] = set()
    for expansion, index, row in iter_material_rows(extra):
        if expansion not in expansions:
            fail(errors, f"Material row uses expansion not in manifest: {expansion}")

        step = (row.get("step") or "").strip()
        material = (row.get("material") or "").strip()
        per_unit = row.get("perUnit")
        jobs = row.get("jobs")

        if not step:
            fail(errors, f"{expansion}[{index}] has no step")
        if not material:
            fail(errors, f"{expansion}[{index}] has no material")
        else:
            material_names.add(material)
        if not isinstance(per_unit, (int, float)) or per_unit <= 0:
            fail(errors, f"{expansion}[{index}] has invalid perUnit: {per_unit!r}")
        if not isinstance(jobs, list) or not jobs or not all(isinstance(flag, bool) for flag in jobs):
            fail(errors, f"{expansion}[{index}] has invalid jobs flags")

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
