using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace LootMaster.Services;

/// <summary>
/// Persists DataGrid column order/widths and grid splitter position to a JSON file.
/// </summary>
public sealed class ColumnSettingsService(string filePath)
{
    private static readonly JsonSerializerOptions _opts = new() { WriteIndented = true };

    private sealed class Settings
    {
        public Dictionary<string, ColumnState> Columns { get; set; } = [];
        public double? SplitterLeft { get; set; }
    }

    public sealed class ColumnState
    {
        public int DisplayIndex { get; set; }
        public double Width { get; set; }
    }

    public void Save(DataGrid grid, ColumnDefinition? leftColumn = null)
    {
        var settings = LoadRaw() ?? new Settings();

        settings.Columns.Clear();
        foreach (var col in grid.Columns)
        {
            string? header = col.Header?.ToString();
            if (string.IsNullOrEmpty(header)) continue;
            settings.Columns[header] = new ColumnState
            {
                DisplayIndex = col.DisplayIndex,
                Width = col.ActualWidth,
            };
        }

        if (leftColumn is not null && leftColumn.ActualWidth > 0)
            settings.SplitterLeft = leftColumn.ActualWidth;

        string dir = Path.GetDirectoryName(filePath)!;
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(filePath, JsonSerializer.Serialize(settings, _opts));
    }

    public void Restore(DataGrid grid, ColumnDefinition? leftColumn = null)
    {
        var settings = LoadRaw();
        if (settings is null) return;

        if (leftColumn is not null && settings.SplitterLeft is double splW && splW > 0)
            leftColumn.Width = new GridLength(splW, GridUnitType.Pixel);

        var state = settings.Columns;

        foreach (var col in grid.Columns)
        {
            string? header = col.Header?.ToString();
            if (header is null || !state.TryGetValue(header, out var s)) continue;
            if (s.Width > 0) col.Width = new DataGridLength(s.Width);
        }

        var ordered = grid.Columns
            .Where(c => c.Header?.ToString() is string h && state.ContainsKey(h))
            .OrderBy(c => state[c.Header!.ToString()!].DisplayIndex)
            .ToList();

        for (int i = 0; i < ordered.Count; i++)
            ordered[i].DisplayIndex = i;
    }

    private Settings? LoadRaw()
    {
        if (!File.Exists(filePath)) return null;
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(filePath), _opts); }
        catch { return null; }
    }
}
