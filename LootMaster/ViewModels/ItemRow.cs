using System.ComponentModel;
using System.Runtime.CompilerServices;
using LootMaster.Models;

namespace LootMaster.ViewModels;

/// <summary>
/// Observable row for the main DataGrid.
/// Wraps ItemInfo + live ItemProgress/CategoryProgress references and exposes
/// the effective (cascade) group and chance values.
/// HighlightState drives the DataGrid row background via DataTrigger.
/// </summary>
public sealed class ItemRow : INotifyPropertyChanged
{
    private ItemProgress _itemProgress;
    private CategoryProgress _categoryProgress;

    public ItemRow(ItemInfo info, ItemProgress itemProgress, CategoryProgress categoryProgress)
    {
        Info = info;
        _itemProgress = itemProgress;
        _categoryProgress = categoryProgress;
    }

    public ItemInfo Info { get; }

    // ── Flat columns exposed directly for DataGrid binding ──────────────────

    public int    ItemId       => Info.ItemId;
    public string ItemName     => Info.ItemName;
    public int    CategoryId   => Info.CategoryId;
    public string CategoryName => Info.CategoryName;
    public string NpcIdsText      => string.Join(", ", Info.NpcIds);
    public string NpcNamesText    => string.Join(", ", Info.NpcNames);
    public string LootPackIdsText => string.Join(", ", Info.LootPackIds);

    // ── Item-level progress ──────────────────────────────────────────────────

    public ItemProgress ItemProgress
    {
        get => _itemProgress;
        set
        {
            _itemProgress = value;
            RaiseAll();
        }
    }

    public int? ItemGroup
    {
        get => _itemProgress.Group;
        set { _itemProgress.Group = value; RaiseAll(); }
    }

    public double? ItemChance
    {
        get => _itemProgress.Chance;
        set { _itemProgress.Chance = value; RaiseAll(); }
    }

    // ── Category-level progress ──────────────────────────────────────────────

    public CategoryProgress CategoryProgress
    {
        get => _categoryProgress;
        set
        {
            _categoryProgress = value;
            RaiseAll();
        }
    }

    public int? CategoryGroup
    {
        get => _categoryProgress.Group;
        set { _categoryProgress.Group = value; RaiseAll(); }
    }

    public double? CategoryChance
    {
        get => _categoryProgress.Chance;
        set { _categoryProgress.Chance = value; RaiseAll(); }
    }

    // ── Current DB values (read-only, loaded at startup) ────────────────────

    public int?    DbGroup  => Info.DbGroup;
    public double? DbChance => Info.DbChance;

    // ── Effective (cascade) values ───────────────────────────────────────────

    /// <summary>Item-level group takes priority; falls back to category group.</summary>
    public int? EffectiveGroup  => _itemProgress.Group ?? _categoryProgress.Group;

    /// <summary>Item-level chance takes priority; falls back to category chance.</summary>
    public double? EffectiveChance => _itemProgress.Chance ?? _categoryProgress.Chance;

    public bool IsProcessed => EffectiveGroup.HasValue;

    // ── Highlight ────────────────────────────────────────────────────────────

    /// <summary>
    /// "Done"     — item has its own group assignment  → green row
    /// "Category" — item inherits group from category → yellow row
    /// ""         — unprocessed                        → default row
    /// </summary>
    public string HighlightState =>
        _itemProgress.Group.HasValue     ? "Done" :
        _categoryProgress.Group.HasValue ? "Category" :
        "";

    // ── INPC ─────────────────────────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(ItemGroup));
        OnPropertyChanged(nameof(ItemChance));
        OnPropertyChanged(nameof(CategoryGroup));
        OnPropertyChanged(nameof(CategoryChance));
        OnPropertyChanged(nameof(EffectiveGroup));
        OnPropertyChanged(nameof(EffectiveChance));
        OnPropertyChanged(nameof(IsProcessed));
        OnPropertyChanged(nameof(HighlightState));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
