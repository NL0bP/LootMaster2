using LootMaster.Models;
using LootMaster.Services;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace LootMaster.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    public event Action<ItemRow>? ScrollIntoViewRequested;

    // ──────────────────────────────────────────────────────────────────────────
    // State
    // ──────────────────────────────────────────────────────────────────────────

    private AppProgress _progress = new();
    private readonly ProgressService _progressSvc;

    // All item rows (source)
    private readonly ObservableCollection<ItemRow> _allRows = new();

    // Filtered view (shown in DataGrid)
    public ICollectionView ItemsView { get; }

    // NPC data for the item currently selected
    public ObservableCollection<string> NpcListItems { get; } = new();
    public ObservableCollection<NpcItemRow> NpcDetailItems { get; } = new();

    // Raw lookup maps (set after loading)
    private Dictionary<int, ItemInfo> _itemsData = new();
    private Dictionary<int, HashSet<int>> _npcToItems = new();
    private Dictionary<int, string> _npcNames = new();

    private ItemRow? _selectedItem;
    private string? _selectedNpcText;
    private int _selectedNpcId;

    // Column visibility
    private bool _showNpcIdColumn = true;
    private bool _showNpcNameColumn = true;

    // Editor fields
    private string _itemGroupText = "";
    private string _itemChanceText = "";
    private string _categoryGroupText = "";
    private string _categoryChanceText = "";

    // Filter
    private string _searchText = "";
    private bool _onlyUnprocessed;
    private bool _highlightCategoryGroup = true;

    // Status
    private string _statusText = "Загрузи SQLite базу и JSON дропа";
    private bool _isLoading;

    private CancellationTokenSource? _loadCts;

    // ──────────────────────────────────────────────────────────────────────────
    // Constructor
    // ──────────────────────────────────────────────────────────────────────────

    public MainViewModel()
    {
        string stateFile = Path.Combine(
            AppContext.BaseDirectory,
            "Data", "loot_group_progress.json");
        _progressSvc = new ProgressService(stateFile);

        ItemsView = CollectionViewSource.GetDefaultView(_allRows);
        ItemsView.Filter = FilterRow;

        // Commands
        OpenDbCommand = new RelayCommand(async () => await OpenDbAsync());
        OpenJsonCommand = new RelayCommand(async () => await OpenJsonAsync());
        SaveProgressCommand = new RelayCommand(async () => await SaveProgressAsync());
        ExportCommand = new RelayCommand(async () => await ExportAsync(), () => _allRows.Count > 0);
        ApplyItemCommand = new RelayCommand(ApplyItemSettings, () => _selectedItem is not null);
        ClearItemCommand = new RelayCommand(ClearItemSettings, () => _selectedItem is not null);
        ApplyCategoryCommand = new RelayCommand(ApplyCategorySettings, () => _selectedItem is not null);
        ClearCategoryCommand = new RelayCommand(ClearCategorySettings, () => _selectedItem is not null);
        PrevCommand = new RelayCommand(SelectPrev);
        NextCommand = new RelayCommand(SelectNext);
        NextUnprocessedCommand = new RelayCommand(SelectNextUnprocessed);
        JumpToNpcItemCommand = new RelayCommand(JumpToSelectedNpcItem);

        _ = RestoreSessionAsync();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Commands
    // ──────────────────────────────────────────────────────────────────────────

    public ICommand OpenDbCommand { get; }
    public ICommand OpenJsonCommand { get; }
    public ICommand SaveProgressCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand ApplyItemCommand { get; }
    public ICommand ClearItemCommand { get; }
    public ICommand ApplyCategoryCommand { get; }
    public ICommand ClearCategoryCommand { get; }
    public ICommand PrevCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand NextUnprocessedCommand { get; }
    public ICommand JumpToNpcItemCommand { get; }

    // ──────────────────────────────────────────────────────────────────────────
    // Bindable properties
    // ──────────────────────────────────────────────────────────────────────────

    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }
    public bool IsLoading { get => _isLoading; set => Set(ref _isLoading, value); }
    public string SearchText { get => _searchText; set { Set(ref _searchText, value); ItemsView.Refresh(); } }
    public bool OnlyUnprocessed { get => _onlyUnprocessed; set { Set(ref _onlyUnprocessed, value); ItemsView.Refresh(); RefreshNpcPanel(); } }
    public bool HighlightCategoryGroup { get => _highlightCategoryGroup; set => Set(ref _highlightCategoryGroup, value); }
    public bool ShowNpcIdColumn { get => _showNpcIdColumn; set => Set(ref _showNpcIdColumn, value); }
    public bool ShowNpcNameColumn { get => _showNpcNameColumn; set => Set(ref _showNpcNameColumn, value); }

    public ItemRow? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (Set(ref _selectedItem, value))
                OnItemSelected(value);
        }
    }

    public string? SelectedNpcText
    {
        get => _selectedNpcText;
        set
        {
            if (Set(ref _selectedNpcText, value))
                OnNpcSelected(value);
        }
    }

    public string ItemGroupText { get => _itemGroupText; set => Set(ref _itemGroupText, value); }
    public string ItemChanceText { get => _itemChanceText; set => Set(ref _itemChanceText, value); }
    public string CategoryGroupText { get => _categoryGroupText; set => Set(ref _categoryGroupText, value); }
    public string CategoryChanceText { get => _categoryChanceText; set => Set(ref _categoryChanceText, value); }

    public ObservableCollection<string> LoadedJsonFiles { get; } = new();

    // Summary text shown in the summary panel
    private string _summaryText = "";
    public string SummaryText { get => _summaryText; private set => Set(ref _summaryText, value); }

    // ──────────────────────────────────────────────────────────────────────────
    // Loading
    // ──────────────────────────────────────────────────────────────────────────

    private async Task RestoreSessionAsync()
    {
        var saved = await _progressSvc.LoadAsync();
        if (saved is null) return;
        _progress = saved;

        // Удаляем из сохранённого списка файлы, которых больше нет на диске
        var missing = _progress.SourceJsonPaths.Where(p => !File.Exists(p)).ToList();
        foreach (var p in missing) _progress.SourceJsonPaths.Remove(p);

        if (!string.IsNullOrEmpty(_progress.DbPath)
            && File.Exists(_progress.DbPath)
            && _progress.SourceJsonPaths.Count > 0)
        {
            await LoadDataAsync(_progress.DbPath, _progress.SourceJsonPaths);
        }
    }

    private async Task OpenDbAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Выбери SQLite базу",
            Filter = "SQLite (*.sqlite3;*.db)|*.sqlite3;*.db|Все файлы|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        _progress.DbPath = dlg.FileName;
        StatusText = $"База: {dlg.FileName}";
        await TrySilentSave();
    }

    private async Task OpenJsonAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Выбери JSON файл(ы) дропа",
            Filter = "JSON (*.json)|*.json|Все файлы|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog() != true) return;

        if (string.IsNullOrEmpty(_progress.DbPath) || !File.Exists(_progress.DbPath))
        {
            MessageBox.Show("Сначала выбери SQLite базу.", "Нет базы", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        foreach (var f in dlg.FileNames)
            if (!_progress.SourceJsonPaths.Contains(f))
                _progress.SourceJsonPaths.Add(f);

        await LoadDataAsync(_progress.DbPath, _progress.SourceJsonPaths);
        await TrySilentSave();
    }

    private async Task LoadDataAsync(string dbPath, IReadOnlyList<string> jsonPaths)
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        IsLoading = true;
        _allRows.Clear();
        NpcListItems.Clear();
        NpcDetailItems.Clear();
        LoadedJsonFiles.Clear();
        foreach (var p in jsonPaths) LoadedJsonFiles.Add(Path.GetFileName(p));

        try
        {
            var progress = new Progress<string>(msg => StatusText = msg);

            // 1. Parse JSON
            StatusText = "Парсим JSON…";
            var parseResult = await JsonSourceParser.ParseAsync(jsonPaths);

            if (parseResult.SkippedFiles.Count > 0)
                MessageBox.Show(
                    $"Пропущены файлы (не является массивом JSON):\n{string.Join('\n', parseResult.SkippedFiles)}",
                    "Предупреждение", MessageBoxButton.OK, MessageBoxImage.Warning);

            var itemIds = parseResult.ItemToNpcs.Keys.OrderBy(x => x).ToList();
            var npcIds = parseResult.NpcToItems.Keys.OrderBy(x => x).ToList();

            StatusText = $"JSON: {itemIds.Count} предметов, {npcIds.Count} NPC. Загружаем имена…";

            var dbSvc = new DatabaseService(dbPath);

            // 2. Load NPC names first (needed by LoadItemsAsync)
            _npcNames = await dbSvc.LoadNpcNamesAsync(npcIds, progress, ct);

            // 3. Load items + categories
            _itemsData = await dbSvc.LoadItemsAsync(itemIds, parseResult.ItemToNpcs, _npcNames, progress, ct);
            _npcToItems = parseResult.NpcToItems;

            // 4. Populate rows on UI thread
            foreach (var id in _itemsData.Keys.OrderBy(x => x))
            {
                var info = _itemsData[id];
                var ip = GetOrCreateItemProgress(id);
                var cp = GetOrCreateCategoryProgress(info.CategoryId);
                _allRows.Add(new ItemRow(info, ip, cp));
            }

            ItemsView.Refresh();
            RefreshSummary();

            // Select first visible
            var first = ItemsView.Cast<ItemRow>().FirstOrDefault();
            if (first is not null)
                SelectedItem = first;

            StatusText = $"Готово. Предметов: {_itemsData.Count}, NPC: {_npcNames.Count}, категорий: {_progress.Categories.Count}";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Загрузка отменена.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка загрузки:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText = "Ошибка загрузки.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Filter
    // ──────────────────────────────────────────────────────────────────────────

    private bool FilterRow(object obj)
    {
        if (obj is not ItemRow row) return false;

        if (_onlyUnprocessed && row.IsProcessed) return false;

        if (string.IsNullOrWhiteSpace(_searchText)) return true;

        var needle = _searchText.Trim().ToLowerInvariant();
        var haystack = $"{row.ItemId} {row.ItemName} {row.CategoryId} {row.CategoryName} {row.NpcIdsText} {row.NpcNamesText}"
                       .ToLowerInvariant();
        return haystack.Contains(needle);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Item selection
    // ──────────────────────────────────────────────────────────────────────────

    private void OnItemSelected(ItemRow? row)
    {
        if (row is null)
        {
            ClearEditor();
            NpcListItems.Clear();
            NpcDetailItems.Clear();
            return;
        }

        // Populate editor
        ItemGroupText = row.ItemGroup?.ToString() ?? "0";
        ItemChanceText = row.ItemChance?.ToString() ?? "100.0";
        CategoryGroupText = row.CategoryGroup?.ToString() ?? "0";
        CategoryChanceText = row.CategoryChance?.ToString() ?? "100.0";

        RefreshNpcPanel();
    }

    private void RefreshNpcPanel()
    {
        if (_selectedItem is null)
        {
            NpcListItems.Clear();
            NpcDetailItems.Clear();
            return;
        }

        NpcListItems.Clear();
        NpcDetailItems.Clear();
        _selectedNpcId = 0;

        var npcIds = GetVisibleNpcIdsForItem(_selectedItem.ItemId);
        foreach (var npcId in npcIds)
        {
            var name = _npcNames.TryGetValue(npcId, out var n) ? n : "";
            var visible = GetVisibleItemIdsForNpc(npcId).Count;
            NpcListItems.Add(string.IsNullOrEmpty(name) ? $"{npcId}  [{visible}]" : $"{npcId} — {name}  [{visible}]");
        }

        if (npcIds.Count > 0)
        {
            _selectedNpcId = npcIds[0];
            PopulateNpcDetail(_selectedNpcId);
            OnPropertyChanged(nameof(SelectedNpcText));
        }
    }

    private void OnNpcSelected(string? text)
    {
        if (text is null || _selectedItem is null) return;
        int idx = NpcListItems.IndexOf(text);
        if (idx < 0) return;
        var npcIds = GetVisibleNpcIdsForItem(_selectedItem.ItemId);
        if (idx >= npcIds.Count) return;
        _selectedNpcId = npcIds[idx];
        PopulateNpcDetail(_selectedNpcId);
    }

    private void PopulateNpcDetail(int npcId)
    {
        NpcDetailItems.Clear();
        foreach (var itemId in GetVisibleItemIdsForNpc(npcId))
        {
            if (!_itemsData.TryGetValue(itemId, out var info)) continue;
            var ip = GetOrCreateItemProgress(itemId);
            var cp = GetOrCreateCategoryProgress(info.CategoryId);
            var tempRow = new ItemRow(info, ip, cp);
            NpcDetailItems.Add(new NpcItemRow(
                itemId,
                info.ItemName,
                info.CategoryName,
                tempRow.EffectiveGroup,
                tempRow.EffectiveChance,
                tempRow.HighlightState));
        }
    }

    private List<int> GetVisibleNpcIdsForItem(int itemId)
    {
        if (!_itemsData.TryGetValue(itemId, out var info)) return [];
        var npcIds = info.NpcIds;
        if (!_onlyUnprocessed) return npcIds;

        return npcIds.Where(npcId =>
            _npcToItems.TryGetValue(npcId, out var items) &&
            items.Any(i => _itemsData.ContainsKey(i) && !GetOrCreateItemRow(i).IsProcessed)
        ).ToList();
    }

    private List<int> GetVisibleItemIdsForNpc(int npcId)
    {
        if (!_npcToItems.TryGetValue(npcId, out var items)) return [];
        var result = items.Where(_itemsData.ContainsKey).OrderBy(x => x).ToList();
        if (_onlyUnprocessed) result = result.Where(i => !GetOrCreateItemRow(i).IsProcessed).ToList();
        return result;
    }

    private ItemRow GetOrCreateItemRow(int itemId)
    {
        var existing = _allRows.FirstOrDefault(r => r.ItemId == itemId);
        if (existing is not null) return existing;
        var info = _itemsData[itemId];
        return new ItemRow(info, GetOrCreateItemProgress(itemId), GetOrCreateCategoryProgress(info.CategoryId));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Editor commands
    // ──────────────────────────────────────────────────────────────────────────

    private void ApplyItemSettings()
    {
        if (_selectedItem is null) return;
        if (!TryParseGroupChance(ItemGroupText, ItemChanceText, out int? grp, out double? chc)) return;

        var ip = GetOrCreateItemProgress(_selectedItem.ItemId);
        ip.Group = grp;
        ip.Chance = chc;
        _selectedItem.ItemProgress = ip;

        _ = TrySilentSave();
        ItemsView.Refresh();
        RefreshSummary();
    }

    private void ClearItemSettings()
    {
        if (_selectedItem is null) return;
        _progress.Items.Remove(_selectedItem.ItemId.ToString());
        _selectedItem.ItemProgress = new ItemProgress();
        ItemGroupText = "";
        ItemChanceText = "";
        _ = TrySilentSave();
        ItemsView.Refresh();
        RefreshSummary();
    }

    private void ApplyCategorySettings()
    {
        if (_selectedItem is null) return;
        if (!TryParseGroupChance(CategoryGroupText, CategoryChanceText, out int? grp, out double? chc)) return;

        var cp = GetOrCreateCategoryProgress(_selectedItem.CategoryId);
        cp.Group = grp;
        cp.Chance = chc;

        // Update all rows that share this category
        foreach (var row in _allRows.Where(r => r.CategoryId == _selectedItem.CategoryId))
            row.CategoryProgress = cp;

        _ = TrySilentSave();
        ItemsView.Refresh();
        RefreshSummary();
    }

    private void ClearCategorySettings()
    {
        if (_selectedItem is null) return;
        _progress.Categories.Remove(_selectedItem.CategoryId.ToString());
        var emptycp = new CategoryProgress();
        foreach (var row in _allRows.Where(r => r.CategoryId == _selectedItem.CategoryId))
            row.CategoryProgress = emptycp;

        CategoryGroupText = "";
        CategoryChanceText = "";
        _ = TrySilentSave();
        ItemsView.Refresh();
        RefreshSummary();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Navigation
    // ──────────────────────────────────────────────────────────────────────────

    private void SelectPrev() => MoveSelection(-1);
    private void SelectNext() => MoveSelection(+1);

    private void MoveSelection(int delta)
    {
        var rows = ItemsView.Cast<ItemRow>().ToList();
        if (rows.Count == 0) return;
        int idx = _selectedItem is null ? 0 : rows.IndexOf(_selectedItem);
        int next = Math.Clamp(idx + delta, 0, rows.Count - 1);
        SelectedItem = rows[next];
        ScrollIntoViewRequested?.Invoke(rows[next]);
    }

    private void SelectNextUnprocessed()
    {
        var rows = ItemsView.Cast<ItemRow>().ToList();
        if (rows.Count == 0) return;
        int start = _selectedItem is null ? -1 : rows.IndexOf(_selectedItem);

        for (int i = start + 1; i < rows.Count; i++)
            if (!rows[i].IsProcessed) { SelectedItem = rows[i]; ScrollIntoViewRequested?.Invoke(rows[i]); return; }
        for (int i = 0; i <= start; i++)
            if (!rows[i].IsProcessed) { SelectedItem = rows[i]; ScrollIntoViewRequested?.Invoke(rows[i]); return; }

        MessageBox.Show("Необработанных предметов больше нет.", "Готово", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void JumpToSelectedNpcItem()
    {
        // Routed from view — NpcItemRow selection resolved via SelectedNpcDetailItem property
        if (SelectedNpcDetailItem is null) return;
        var target = _allRows.FirstOrDefault(r => r.ItemId == SelectedNpcDetailItem.ItemId);
        if (target is null) return;

        // Clear filters so the target row is visible
        SearchText = "";
        OnlyUnprocessed = false;

        SelectedItem = target;

        // Defer scroll until after ItemsView.Refresh() is processed by the UI
        Application.Current.Dispatcher.InvokeAsync(
            () => ScrollIntoViewRequested?.Invoke(target),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private NpcItemRow? _selectedNpcDetailItem;
    public NpcItemRow? SelectedNpcDetailItem
    {
        get => _selectedNpcDetailItem;
        set => Set(ref _selectedNpcDetailItem, value);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Save / Export
    // ──────────────────────────────────────────────────────────────────────────

    private async Task SaveProgressAsync()
    {
        try
        {
            await _progressSvc.SaveAsync(_progress);
            StatusText = "Прогресс сохранён.";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка сохранения:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task TrySilentSave()
    {
        try { await _progressSvc.SaveAsync(_progress); }
        catch { /* silent */ }
    }

    private async Task ExportAsync()
    {
        var dlg = new SaveFileDialog
        {
            Title = "Сохранить результат",
            DefaultExt = ".json",
            Filter = "JSON (*.json)|*.json|Все файлы|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        var items = _allRows
            .Select(r => (r.ItemId, r.EffectiveChance))
            .ToList();

        try
        {
            await ProgressService.ExportAsync(dlg.FileName, items);
            StatusText = $"Экспорт сохранён: {dlg.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка экспорта:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Summary
    // ──────────────────────────────────────────────────────────────────────────

    private void RefreshSummary()
    {
        int total = _allRows.Count;
        int done = 0, itemDone = 0, catDone = 0, withChance = 0;

        foreach (var row in _allRows)
        {
            if (row.ItemProgress.Group.HasValue) { done++; itemDone++; }
            else if (row.CategoryProgress.Group.HasValue) { done++; catDone++; }
            if (row.EffectiveChance.HasValue) withChance++;
        }

        SummaryText =
            $"Всего предметов: {total}\n" +
            $"Обработано: {done}\n" +
            $"  напрямую: {itemDone}\n" +
            $"  через категорию: {catDone}\n" +
            $"С итоговым шансом: {withChance}";
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private ItemProgress GetOrCreateItemProgress(int itemId)
    {
        var key = itemId.ToString();
        if (!_progress.Items.TryGetValue(key, out var ip))
        {
            ip = new ItemProgress();
            _progress.Items[key] = ip;
        }
        return ip;
    }

    private CategoryProgress GetOrCreateCategoryProgress(int catId)
    {
        var key = catId.ToString();
        if (!_progress.Categories.TryGetValue(key, out var cp))
        {
            cp = new CategoryProgress();
            _progress.Categories[key] = cp;
        }
        return cp;
    }

    private static bool TryParseGroupChance(
        string groupText, string chanceText,
        out int? group, out double? chance)
    {
        group = null;
        chance = null;

        groupText = groupText.Trim();
        chanceText = chanceText.Trim().Replace(',', '.');

        if (!string.IsNullOrEmpty(groupText))
        {
            if (!int.TryParse(groupText, out int g))
            {
                MessageBox.Show("Группа должна быть целым числом.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            group = g;
        }

        if (!string.IsNullOrEmpty(chanceText))
        {
            if (!double.TryParse(chanceText, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double c))
            {
                MessageBox.Show("Шанс должен быть числом.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            chance = c;
        }

        return true;
    }

    private void ClearEditor()
    {
        ItemGroupText = "";
        ItemChanceText = "";
        CategoryGroupText = "";
        CategoryChanceText = "";
    }

    // ──────────────────────────────────────────────────────────────────────────
    // INPC
    // ──────────────────────────────────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

// ──────────────────────────────────────────────────────────────────────────────
// Helper DTO for the NPC items sub-grid
// ──────────────────────────────────────────────────────────────────────────────

public sealed class NpcItemRow(
    int itemId,
    string itemName,
    string categoryName,
    int? effectiveGroup,
    double? effectiveChance,
    string highlightState)
{
    public int ItemId { get; } = itemId;
    public string ItemName { get; } = itemName;
    public string CategoryName { get; } = categoryName;
    public int? EffectiveGroup { get; } = effectiveGroup;
    public double? EffectiveChance { get; } = effectiveChance;
    public string HighlightState { get; } = highlightState;
}
