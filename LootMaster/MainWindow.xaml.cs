using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LootMaster.Services;
using LootMaster.ViewModels;

namespace LootMaster;

public partial class MainWindow : Window
{
    private MainViewModel _vm = null!;
    private readonly ColumnSettingsService _colSettings = new(
        Path.Combine(AppContext.BaseDirectory, "Data", "column-settings.json"));

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;

        _vm.ScrollIntoViewRequested += row =>
        {
            ItemsGrid.ScrollIntoView(row);
            ItemsGrid.Focus();
        };

        Loaded += (_, _) =>
        {
            _colSettings.Restore(ItemsGrid, MainGridLeft);
            ItemsGrid.Focus();
        };

        Closing += (_, _) => _colSettings.Save(ItemsGrid, MainGridLeft);

        ItemsGrid.ColumnReordered += (_, _) => _colSettings.Save(ItemsGrid, MainGridLeft);

        NpcDetailGrid.SelectionChanged += OnNpcDetailSelectionChanged;
        NpcDetailGrid.MouseDoubleClick += OnNpcDetailDoubleClick;
        NpcListBox.SelectionChanged += (_, _) => ItemsGrid.Focus();
    }

    private void OnNpcDetailSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ItemsGrid.Focus();
    }

    private void OnNpcDetailDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_vm.JumpToNpcItemCommand.CanExecute(null))
            _vm.JumpToNpcItemCommand.Execute(null);
    }
}
