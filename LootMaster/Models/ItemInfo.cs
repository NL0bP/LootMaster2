namespace LootMaster.Models;

public class ItemInfo
{
    public int ItemId { get; init; }
    public string ItemName { get; init; } = "";
    public int CategoryId { get; init; }
    public string CategoryName { get; init; } = "";
    public List<int> NpcIds { get; init; } = new();
    public List<string> NpcNames { get; init; } = new();

    /// <summary>Loot pack IDs linked to this item via loot_pack_dropping_npcs.</summary>
    public List<int> LootPackIds { get; init; } = new();

    /// <summary>Current group value from the loots table (null if not found).</summary>
    public int? DbGroup { get; init; }

    /// <summary>Current drop chance from the loots table in percent (null if not found).</summary>
    public double? DbChance { get; init; }
}
