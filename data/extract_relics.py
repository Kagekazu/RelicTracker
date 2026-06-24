#!/usr/bin/env python3
"""Build the canonical relic catalog (relic_lines.json) from the FFXIV Collect API.

FFXIV Collect groups relics into *types* (e.g. "Anima Weapons"). Within a type,
relics are ordered by `order` (1-based, contiguous) and sliced into tiers of size
`jobs`. So tier count = len(relics) / jobs, and a relic at rank r (0-based) sits at
tier = r // jobs, job slot = r % jobs.

This script derives the structure (tier counts, job counts, category, expansion)
straight from the API, and layers human-readable step names from the authored
tables below. The plugin's Overview reads the result to show, per relic line, how
many jobs sit at each step.
"""

from __future__ import annotations

import json
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parent
OUT = ROOT / "extracted" / "relic_lines.json"
RAW_CACHE = ROOT / "extracted" / "_relics_index_raw.json"
API_URL = "https://ffxivcollect.com/api/relics?limit=3000"

# Map FFXIV Collect's numeric expansion + type into Wyn's expansion tabs.
# Tools all live under the DoHDoL tab regardless of release expansion.
TYPE_TO_EXPANSION = {
    "A Relic Reborn": "ARR",
    "Anima Weapons": "HW",
    "Eureka Weapons": "SB",
    "Resistance Weapons": "ShB",
    "Manderville Weapons": "EW",
    "Phantom Weapons": "DT",
    "Lucis Tools": "DoHDoL",
    "Skysteel Tools": "DoHDoL",
    "Resplendent Tools": "DoHDoL",
    "Splendorous Tools": "DoHDoL",
    "Cosmic Tools": "DoHDoL",
}

# Authored step names per tier, validated against the API tier counts.
STEP_NAMES = {
    "A Relic Reborn": ["Relic", "Zenith", "Atma", "Animus", "Novus", "Nexus", "Zodiac", "Zeta"],
    "Anima Weapons": ["Animated", "Awoken", "Anima", "Hyperconductive", "Reconditioned", "Sharpened", "Complete", "Lux"],
    "Eureka Weapons": [
        "Base", "Base +1", "Base +2", "Anemos", "Pagos", "Pagos +1",
        "Elemental", "Elemental +1", "Elemental +2", "Pyros", "Hydatos", "Hydatos +1",
        "Base (Physeos)", "Eureka", "Physeos",
    ],
    "Resistance Weapons": ["Resistance", "Augmented", "Recollection", "Law's Order", "Augmented Law's Order", "Blade's"],
    "Manderville Weapons": ["Manderville", "Amazing", "Majestic", "Mandervillous"],
    "Phantom Weapons": ["Penumbrae", "Umbrae", "Obscurum"],
    "Lucis Tools": ["Mastercraft", "Supra", "Lucis"],
    "Skysteel Tools": ["Skysteel", "Skysteel +1", "Dragonsung", "Augmented Dragonsung", "Skysung", "Skybuilders'"],
    "Resplendent Tools": ["Resplendent"],
    "Splendorous Tools": ["Splendorous", "Augmented", "Crystalline", "Chora-Zoi's", "Brilliant", "Vrandtic", "Lodestar"],
    "Cosmic Tools": ["Cosmic", "Stellar", "Hyper", "Stellar (final)"],
}

# Per-line job slot lists, ordered to match FFXIV Collect's within-tier slot order
# (classic relic order — verified against tier-0 relic names). Length must equal `jobs`.
DOH_DOL = ["CRP", "BSM", "ARM", "GSM", "LTW", "WVR", "ALC", "CUL", "MIN", "BTN", "FSH"]
JOB_LISTS = {
    "A Relic Reborn": ["PLD", "MNK", "WAR", "DRG", "BRD", "WHM", "BLM", "SMN", "SCH", "NIN"],
    "Anima Weapons": ["PLD", "MNK", "WAR", "DRG", "BRD", "NIN", "DRK", "MCH", "WHM", "AST", "BLM", "SMN", "SCH"],
    "Eureka Weapons": ["PLD", "MNK", "WAR", "DRG", "BRD", "NIN", "DRK", "MCH", "WHM", "AST", "BLM", "SMN", "SCH", "SAM", "RDM"],
    "Resistance Weapons": ["PLD", "MNK", "WAR", "DRG", "BRD", "NIN", "DRK", "MCH", "WHM", "AST", "BLM", "SMN", "SCH", "SAM", "RDM", "GNB", "DNC"],
    "Manderville Weapons": ["PLD", "MNK", "WAR", "DRG", "BRD", "NIN", "DRK", "MCH", "WHM", "AST", "BLM", "SMN", "SCH", "SAM", "RDM", "GNB", "DNC", "RPR", "SGE"],
    "Phantom Weapons": ["PLD", "MNK", "WAR", "DRG", "BRD", "NIN", "DRK", "MCH", "WHM", "AST", "BLM", "SMN", "SCH", "SAM", "RDM", "GNB", "DNC", "RPR", "SGE", "VPR", "PCT"],
    "Lucis Tools": DOH_DOL,
    "Skysteel Tools": DOH_DOL,
    "Resplendent Tools": DOH_DOL,
    "Splendorous Tools": DOH_DOL,
    "Cosmic Tools": DOH_DOL,
}

# Field-operation relic armor, curated by expansion. Each line groups DISTINCT armor
# SETS (these are separate sets, not one upgrade chain); within a set, "Augmented" /
# "+1" / "+2" are augment tiers of the same set. Pieces come from the API. Excludes
# GARO event armor and the unrelated "Idealized" Bozja gear. "Antiquated Artifact" /
# "Occult Accessories" aren't tracked as collectibles on FFXIV Collect, so they're omitted.
ARMOR_LINES = [
    # Main progression (Antiquated AF -> Base -> +1 -> +2 -> Anemos; Antiquated isn't a
    # FFXIV Collect collectible so tracked tiers start at Base), plus the separate
    # 35-piece Elemental set.
    ("SB", "Eurekan Armor", [
        ("Eurekan", [
            ("Eureka Job Armor", "Base"),
            ("Eureka Job Armor +1", "+1"),
            ("Eureka Job Armor +2", "+2"),
            ("Eureka Anemos Armor", "Anemos"),
        ]),
        ("Elemental", [
            ("Elemental Armor", "Base"),
            ("Elemental Armor +1", "+1"),
            ("Elemental Armor +2", "+2"),
        ]),
    ]),
    ("ShB", "Resistance Armor", [
        ("Bozjan", [("Bozjan Armor", "Base"), ("Augmented Bozjan Armor", "Augmented")]),
        ("Law's Order", [("Law's Order", "Base"), ("Augmented Law's Order", "Augmented")]),
        ("Blade's", [("Blade's Armor", "Base")]),
    ]),
    ("DT", "Phantom Armor", [
        ("Arcanaut's", [
            ("Arcanaut's Armor", "Base"),
            ("Arcanaut's Armor +1", "+1"),
            ("Arcanaut's Armor +2", "+2"),
        ]),
    ]),
]

# Display order of expansions in the catalog.
EXPANSION_ORDER = ["ARR", "HW", "SB", "ShB", "EW", "DT", "DoHDoL"]


def load_index() -> list[dict]:
    if RAW_CACHE.exists():
        payload = json.loads(RAW_CACHE.read_text(encoding="utf-8"))
    else:
        request = urllib.request.Request(API_URL, headers={"User-Agent": "RelicTracker/0.1"})
        payload = json.loads(urllib.request.urlopen(request, timeout=30).read())
        RAW_CACHE.write_text(json.dumps(payload), encoding="utf-8")
    return payload.get("results", payload) if isinstance(payload, dict) else payload


def build_lines(relics: list[dict]) -> list[dict]:
    grouped: dict[str, dict] = {}
    for relic in relics:
        type_info = relic.get("type") or {}
        name = type_info.get("name")
        if name not in TYPE_TO_EXPANSION:
            continue
        entry = grouped.setdefault(
            name,
            {
                "collectType": name,
                "category": type_info.get("category"),
                "jobs": type_info.get("jobs") or 0,
                "expansion": TYPE_TO_EXPANSION[name],
                "typeOrder": type_info.get("order") or 0,
                "relics": [],
            },
        )
        entry["relics"].append((relic.get("order") or 0, relic.get("name") or ""))

    lines: list[dict] = []
    for name, entry in grouped.items():
        jobs = entry["jobs"]
        ordered = sorted(entry["relics"])
        total = len(ordered)
        tier_count = total // jobs if jobs else 0
        # Tier-0 relic names in slot order — used at runtime to resolve slot -> job.
        slot_relics = [relic_name for _, relic_name in ordered[:jobs]]
        authored = STEP_NAMES.get(name, [])
        steps = [
            authored[i] if i < len(authored) else f"Step {i + 1}"
            for i in range(tier_count)
        ]
        job_list = JOB_LISTS.get(name, [])
        if job_list and len(job_list) != jobs:
            raise ValueError(f"{name}: job list has {len(job_list)} entries, expected {jobs}")
        lines.append(
            {
                "collectType": name,
                "category": entry["category"],
                "expansion": entry["expansion"],
                "jobs": jobs,
                "tierCount": tier_count,
                "relicCount": total,
                "steps": steps,
                "jobList": job_list,
                "slotRelics": slot_relics,
                "typeOrder": entry["typeOrder"],
            }
        )

    lines.sort(key=lambda line: (EXPANSION_ORDER.index(line["expansion"]), line["typeOrder"]))
    return lines


def build_armor(relics: list[dict]) -> list[dict]:
    counts: dict[str, int] = {}
    for relic in relics:
        name = (relic.get("type") or {}).get("name")
        if name:
            counts[name] = counts.get(name, 0) + 1

    lines: list[dict] = []
    for expansion, line_name, sets in ARMOR_LINES:
        resolved_sets = []
        for set_name, tiers in sets:
            resolved_tiers = []
            for collect_type, label in tiers:
                pieces = counts.get(collect_type, 0)
                if pieces == 0:
                    raise ValueError(f"Armor type not found in index: {collect_type}")
                resolved_tiers.append({"collectType": collect_type, "label": label, "pieces": pieces})
            resolved_sets.append({"name": set_name, "tiers": resolved_tiers})
        lines.append({"expansion": expansion, "lineName": line_name, "sets": resolved_sets})
    return lines


def main() -> None:
    relics = load_index()
    lines = build_lines(relics)
    OUT.write_text(json.dumps(lines, indent=2) + "\n", encoding="utf-8")
    print(f"Wrote {len(lines)} relic lines to {OUT}")
    for line in lines:
        print(f"  {line['expansion']:7} {line['collectType']:20} jobs={line['jobs']:2} tiers={line['tierCount']}")

    armor = build_armor(relics)
    armor_out = OUT.parent / "relic_armor.json"
    armor_out.write_text(json.dumps(armor, indent=2) + "\n", encoding="utf-8")
    print(f"\nWrote {len(armor)} armor lines to {armor_out}")
    for line in armor:
        total = sum(t["pieces"] for s in line["sets"] for t in s["tiers"])
        set_names = ", ".join(s["name"] for s in line["sets"])
        print(f"  {line['expansion']:5} {line['lineName']:18} sets={len(line['sets'])} pieces={total}  [{set_names}]")


if __name__ == "__main__":
    main()
