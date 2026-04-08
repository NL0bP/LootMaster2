# LootMaster 2.0

A WPF desktop tool for assigning loot groups and drop chances to ArcheAge items.
Loads NPC drop tables from JSON files and item/NPC metadata from an SQLite database.

![LootMaster 2.0 screenshot](Docs/screenshot.png)

---

## Features

- Load multiple NPC loot JSON files simultaneously — duplicates are deduplicated automatically
- Assign loot group and drop chance per item or per entire category at once
- Category-level assignments cascade to all items; item-level assignments take priority
- Real-time search and filter (by item name, category, NPC ID/name)
- "Only unprocessed" filter to focus on remaining work
- NPC browser — see all NPCs that drop the selected item, and all items a selected NPC drops
- Jump to any item from the NPC browser with a double-click
- Auto-save after every change; session is restored automatically on next launch
- Export results to JSON for use in the server emulator

---

## Requirements

- Windows 10 or later
- [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)

---

## Getting Started

1. **Open SQLite database** — click "Открыть SQLite базу" and select your `compact.server.table.sqlite3` file
2. **Open loot JSON file(s)** — click "Открыть JSON дропа" and select one or more loot files

Expected loot JSON format:
```json
[
  {
    "npc_id": 123,
    "items": [
      { "item_id": 456 },
      { "item_id": 789 }
    ]
  }
]
```

3. The table populates with all items and their associated NPCs
4. Select an item, fill in group/chance in the right panel, click **Применить**
5. Use **Применить ко всей категории** to apply the same values to all items in a category
6. Press **Ctrl+S** to save, or use **Экспорт результата** to export the final JSON

---

## Data Files

All data is stored in the `Data\` folder next to the executable:

```
LootMaster.exe
Data\
  loot_group_progress.json    ← working progress file (auto-created)
```

---

## Export Format

```json
{
  "items": [
    { "item_id": 456, "chance": 0.5 },
    { "item_id": 789, "chance": 1.0 }
  ]
}
```

---

## Keyboard Shortcuts

| Shortcut | Action |
|---|---|
| **Ctrl+S** | Save progress |

---

## Built With

- .NET 10 / C# 14
- WPF
- [Microsoft.Data.Sqlite](https://www.nuget.org/packages/Microsoft.Data.Sqlite)
- System.Text.Json

---

## Documentation

See [Docs/user-guide.md](Docs/user-guide.md) for full usage instructions.
