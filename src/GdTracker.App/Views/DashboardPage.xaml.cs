using System.Windows.Controls;
using GdTracker.ViewModels;

namespace GdTracker.App.Views;

public partial class DashboardPage : Page
{
    private readonly LevelsViewModel _viewModel;

    public DashboardPage(LevelsViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Loaded += async (_, _) => await _viewModel.LoadAsync();
    }

    // Синхронизация выделения мышью (Ctrl/Shift) с IsSelected строк для группового удаления.
    private void OnLevelsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        foreach (var item in e.RemovedItems)
            if (item is LevelRowViewModel row)
                row.IsSelected = false;

        foreach (var item in e.AddedItems)
            if (item is LevelRowViewModel row)
                row.IsSelected = true;
    }
}
