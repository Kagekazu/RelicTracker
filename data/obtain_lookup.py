"""Derive how an item is obtained (craft / gather / fish / buy / content) from game sheets.

Used by gen_tool_materials.py to tag every material row with `obtain` (+ `obtainVia`). Sheets
come from xivapi/ffxiv-datamining and are cached under data/extracted/_sheets/.
"""

from __future__ import annotations

import csv
import io
import urllib.request
from pathlib import Path

SHEET_URL = "https://raw.githubusercontent.com/xivapi/ffxiv-datamining/master/csv/en/{}.csv"
SHEETS = ["Recipe", "GatheringItem", "FishParameter", "SpearfishingItem", "GilShopItem",
          "GCScripShopItem", "SpecialShop", "TomestonesItem"]

# Obtain kinds, in the order a row picks its primary one when several apply.
KINDS = ["fish", "gather", "buy", "craft", "content"]

# Currencies a vendor run can spend (mirrors Knightshopper's currency list).
CURRENCY_ITEMS = {
    1: "Gil",
    20: "GC seals", 21: "GC seals", 22: "GC seals",
    25: "PvP", 36656: "PvP",
    27: "Hunt seals", 10307: "Hunt seals", 26533: "Hunt seals",
    29: "MGP",
    26807: "Bicolor Gemstones",
    28063: "Skybuilders' Scrips",
    45690: "Cosmocredits",
}

# When an item is sold for several currencies, the earlier one wins.
CURRENCY_PREFERENCE = ["Gil", "Tomestones", "Scrips", "GC seals", "Bicolor Gemstones",
                       "Skybuilders' Scrips", "Hunt seals", "Cosmocredits", "MGP", "PvP"]

# SpecialShop CostType: 0/1 = item ID (NQ/HQ), 2 = tomestone slot, 3 = special currency (scrips).
COST_TYPE_TOMESTONE = "2"
COST_TYPE_SPECIAL_CURRENCY = "3"


def _rows(text: str):
    reader = csv.reader(io.StringIO(text))
    header = next(reader)
    return header, [row for row in reader if row and row[0][:1].isdigit()]


def _load(cache: Path, name: str) -> tuple[list[str], list[list[str]]]:
    path = cache / f"{name}.csv"
    if not path.exists():
        print(f"Downloading {name} sheet ...")
        request = urllib.request.Request(SHEET_URL.format(name), headers={"User-Agent": "RelicTracker/0.1"})
        with urllib.request.urlopen(request, timeout=300) as response:
            text = response.read().decode("utf-8-sig")
        cache.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8")
    return _rows(path.read_text(encoding="utf-8-sig"))


class ObtainIndex:
    def __init__(self, cache: Path, item_names: dict[int, str]) -> None:
        self._ways: dict[int, set[tuple[str, str]]] = {}
        sheets = {name: _load(cache, name) for name in SHEETS}

        header, rows = sheets["Recipe"]
        col = header.index("ItemResult")
        for row in rows:
            self._add(int(row[col] or 0), "craft")

        header, rows = sheets["GatheringItem"]
        col = header.index("Item")
        for row in rows:
            self._add(int(row[col] or 0), "gather")

        for name in ("FishParameter", "SpearfishingItem"):
            header, rows = sheets[name]
            col = header.index("Item")
            for row in rows:
                self._add(int(row[col] or 0), "fish")

        _, rows = sheets["GilShopItem"]
        for row in rows:
            self._add(int(row[1] or 0), "buy", "Gil")

        header, rows = sheets["GCScripShopItem"]
        col = header.index("Item")
        for row in rows:
            self._add(int(row[col] or 0), "buy", "GC seals")

        header, rows = sheets["TomestonesItem"]
        tomestones = {int(row[header.index("Item")] or 0) for row in rows}

        header, rows = sheets["SpecialShop"]
        idx = {name: i for i, name in enumerate(header)}
        for row in rows:
            for n in range(60):
                prefix = f"Item[{n}]."
                if prefix + "Item[0]" not in idx:
                    break
                received = int(row[idx[prefix + "Item[0]"]] or 0)
                if not received:
                    continue
                for c in range(3):
                    cost_id = int(row[idx[f"{prefix}ItemCost[{c}]"]] or 0)
                    amount = row[idx[f"{prefix}CurrencyCost[{c}]"]]
                    cost_type = row[idx[f"{prefix}CostType[{c}]"]]
                    if not cost_id or amount in ("", "0"):
                        continue
                    if cost_type == COST_TYPE_TOMESTONE or cost_id in tomestones:
                        self._add(received, "buy", "Tomestones")
                    elif cost_type == COST_TYPE_SPECIAL_CURRENCY:
                        self._add(received, "buy", "Scrips")
                    elif cost_id in CURRENCY_ITEMS:
                        self._add(received, "buy", CURRENCY_ITEMS[cost_id])
                    else:
                        # Traded for a content token (Anemos Crystal, cluster, ...).
                        self._add(received, "content", item_names.get(cost_id, ""))

    def _add(self, item_id: int, kind: str, via: str = "") -> None:
        if item_id:
            self._ways.setdefault(item_id, set()).add((kind, via))

    def ways(self, item_ids: list[int]) -> set[tuple[str, str]]:
        found: set[tuple[str, str]] = set()
        for item_id in item_ids:
            found |= self._ways.get(item_id, set())
        return found

    def primary(self, item_ids: list[int]) -> tuple[str, str]:
        """Best single way to get the item; items no sheet knows are content drops."""
        ways = self.ways(item_ids)
        for kind in KINDS:
            vias = sorted(via for k, via in ways if k == kind)
            if vias:
                ranked = [via for via in CURRENCY_PREFERENCE if via in vias]
                return kind, ranked[0] if ranked else vias[0]
        return "content", ""
