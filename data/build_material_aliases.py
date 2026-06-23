#!/usr/bin/env python3
"""Match Wyn material names to Garland Tools item names for alias generation."""

from __future__ import annotations

import json
import time
import urllib.parse
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parent
EXPANSIONS = ROOT / "extracted" / "expansions.json"
OUT = ROOT / "extracted" / "material_aliases.json"

SKIP = {
    "Select Material",
    "Crafters",
    "Fisher",
    "Miner & Botanist",
    "Cosmic",
    "You just do Cosmic Exploration.",
}


def collect_material_names() -> list[str]:
    data = json.loads(EXPANSIONS.read_text(encoding="utf-8"))
    names: set[str] = set()
    for sheet in data.values():
        for row in sheet.get("materials", []):
            name = (row.get("material") or "").strip()
            if not name or len(name) > 60 or "\n" in name:
                continue
            if name.startswith("First ") or "assume" in name.lower():
                continue
            if name in SKIP:
                continue
            names.add(name)
    return sorted(names)


def garland_search(query: str) -> str | None:
    url = (
        "https://www.garlandtools.org/api/search?"
        + urllib.parse.urlencode({"q": query, "type": "Item"})
    )
    request = urllib.request.Request(url, headers={"User-Agent": "RelicTracker/0.1"})
    payload = json.loads(urllib.request.urlopen(request, timeout=20).read())
    objects = payload.get("objects") or []
    if not objects:
        return None
    return objects[0].get("obj", {}).get("n")


def resolve_name(wyn_name: str) -> str | None:
    candidates = [wyn_name]
    if wyn_name.endswith(" Parts"):
        candidates.append(wyn_name[:-1])
    if wyn_name.endswith(" Pars"):
        candidates.append(wyn_name.replace(" Pars", " Part"))
        candidates.append(wyn_name.replace(" Pars", " Parts"))
    if wyn_name.endswith(" Shard"):
        candidates.append(wyn_name + "s")

    seen: set[str] = set()
    for candidate in candidates:
        if candidate in seen:
            continue
        seen.add(candidate)
        hit = garland_search(candidate)
        if hit:
            return hit
        time.sleep(0.12)
    return None


def main() -> None:
    aliases: dict[str, str] = {}
    manual: dict[str, str] = {
        "Oddly Delicate Parts": "Oddly Delicate Coffer",
        "Oddly Specific Material #1": "Oddly Specific Coffer",
        "Oddly Specific Material #2": "Oddly Specific Coffer",
        "Oddly Specific Material #3": "Oddly Specific Coffer",
        "Oddly Specific Material #4": "Oddly Specific Coffer",
        "Splendorous Fishing Parts": "Splendorous Fishing Rod Component",
        "Adaptive Fishing Parts": "Adaptive Fishing Rod Component",
        "Brilliant Fishing Parts": "Brilliant Fishing Rod Component",
        "Customized Fishing Parts": "Customized Fishing Rod Component",
        "Inspirational Fishing Parts": "Inspirational Fishing Rod Component",
        "Nightforged Fishing Pars": "Nightforged Fishing Rod Part",
        "Cosmic v1.1": "Cosmic Prototype v1.1",
        "Cosmic v1.2": "Cosmic Prototype v1.2",
        "Cosmic v1.3": "Cosmic Prototype v1.3",
        "Cosmic v1.4": "Cosmic Prototype v1.4",
        "Stellar v1.1": "Stellar Prototype v1.1",
        "Stellar v1.2": "Stellar Prototype v1.2",
        "Prototype v0.1": "Prototype Skybuilders' Part v0.1",
        "Prototype v0.2": "Prototype Skybuilders' Part v0.2",
        "Prototype v0.3": "Prototype Skybuilders' Part v0.3",
        "Prototype v0.4": "Prototype Skybuilders' Part v0.4",
        "Prototype v0.5": "Prototype Skybuilders' Part v0.5",
        "Prototype v0.6": "Prototype Skybuilders' Part v0.6",
        "Prototype v0.7": "Prototype Skybuilders' Part v0.7",
        "Prototype v0.8": "Prototype Skybuilders' Part v0.8",
    }

    for wyn_name in collect_material_names():
        if wyn_name in manual:
            aliases[wyn_name] = manual[wyn_name]
            continue
        hit = resolve_name(wyn_name)
        if hit and hit.lower() != wyn_name.lower():
            aliases[wyn_name] = hit
        print(f"{wyn_name!r} -> {hit!r}")

    OUT.write_text(json.dumps(aliases, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(f"\nWrote {len(aliases)} aliases to {OUT}")


if __name__ == "__main__":
    main()
