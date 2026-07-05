"""Resolve English item names to row IDs for bundled relic/armor data."""

from __future__ import annotations

import csv
import io
import urllib.request
from pathlib import Path

DEFAULT_ITEM_CSV_URL = (
    "https://raw.githubusercontent.com/xivapi/ffxiv-datamining/master/csv/en/Item.csv"
)


def expand_name_variants(name: str):
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


class ItemIndex:
    def __init__(self) -> None:
        self._by_name: dict[str, int] = {}
        self._replicas_by_base: dict[str, list[int]] = {}

    @classmethod
    def from_csv_path(cls, path: Path) -> ItemIndex:
        text = path.read_text(encoding="utf-8-sig")
        return cls.from_csv_text(text)

    @classmethod
    def from_csv_text(cls, text: str) -> ItemIndex:
        index = cls()
        reader = csv.DictReader(io.StringIO(text))
        if not reader.fieldnames:
            raise ValueError("Item CSV has no header row.")

        id_field = "#" if "#" in reader.fieldnames else None
        name_field = "Name" if "Name" in reader.fieldnames else None
        if id_field is None or name_field is None:
            raise ValueError("Item CSV must include '#' and 'Name' columns.")

        for row in reader:
            name = (row.get(name_field) or "").strip()
            raw_id = (row.get(id_field) or "").strip()
            if not name or not raw_id:
                continue

            try:
                item_id = int(raw_id)
            except ValueError:
                continue

            index._by_name.setdefault(name.casefold(), item_id)
            if name.lower().startswith("hq "):
                index._by_name.setdefault(name[3:].strip().casefold(), item_id)
            if name.startswith("Replica "):
                base = name[len("Replica ") :].strip()
                if base:
                    replicas = index._replicas_by_base.setdefault(base.casefold(), [])
                    if item_id not in replicas:
                        replicas.append(item_id)

        return index

    def resolve(self, name: str) -> int:
        for candidate in expand_name_variants(name.strip()):
            item_id = self._by_name.get(candidate.casefold())
            if item_id is not None:
                return item_id
        raise KeyError(name)

    def replicas_for(self, base_name: str) -> list[int]:
        return list(self._replicas_by_base.get(base_name.strip().casefold(), []))


def fetch_item_csv(url: str = DEFAULT_ITEM_CSV_URL) -> str:
    request = urllib.request.Request(url, headers={"User-Agent": "RelicTracker/0.1"})
    with urllib.request.urlopen(request, timeout=120) as response:
        return response.read().decode("utf-8-sig")


def alias_targets(value) -> list[str]:
    if isinstance(value, str):
        return [value]
    if isinstance(value, list):
        return [item for item in value if isinstance(item, str)]
    return []


def material_ids_for(index: ItemIndex, aliases: dict, material_name: str) -> list[int]:
    """All item row IDs that count toward owned for a tracked material (includes alias expansion)."""
    targets = alias_targets(aliases.get(material_name, material_name))
    ids: list[int] = []
    seen: set[int] = set()
    for target in targets:
        for variant in expand_name_variants(target):
            try:
                item_id = index.resolve(variant)
            except KeyError:
                continue
            if item_id not in seen:
                seen.add(item_id)
                ids.append(item_id)
    if not ids:
        raise KeyError(material_name)
    return ids


def attach_material_row(row: dict, index: ItemIndex, aliases: dict) -> None:
    material = (row.get("material") or "").strip()
    if material:
        row["materialIds"] = material_ids_for(index, aliases, material)


def attach_tool_extra_materials(data: dict, index: ItemIndex, aliases: dict) -> None:
    for rows in data.values():
        for row in rows:
            attach_material_row(row, index, aliases)


def attach_armor_costs(data: dict, index: ItemIndex, aliases: dict | None = None) -> None:
    aliases = aliases or {}
    for costs in data.values():
        for cost in costs:
            currency = (cost.get("currency") or "").strip()
            if currency:
                cost["currencyIds"] = material_ids_for(index, aliases, currency)


def load_item_index(csv_path: Path, *, allow_download: bool = True) -> ItemIndex:
    if csv_path.exists():
        return ItemIndex.from_csv_path(csv_path)

    if not allow_download:
        raise FileNotFoundError(
            f"Item CSV not found at {csv_path}. "
            "Run extract_relics.py once with network access to cache it."
        )

    print(f"Downloading item sheet to {csv_path} ...")
    csv_path.parent.mkdir(parents=True, exist_ok=True)
    text = fetch_item_csv()
    csv_path.write_text(text, encoding="utf-8")
    return ItemIndex.from_csv_text(text)
