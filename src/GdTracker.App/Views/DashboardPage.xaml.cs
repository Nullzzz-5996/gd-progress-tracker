using System.Windows;
using System.Windows.Controls;
using GdTracker.ViewModels;
using Wpf.Ui;

namespace GdTracker.App.Views;

public partial class DashboardPage : Page
{
    private readonly LevelsViewModel _viewModel;
    private readonly INavigationService _navigation;
    private readonly IProgressNavigationContext _progressContext;

    public DashboardPage(
        LevelsViewModel viewModel,
        INavigationService navigation,
        IProgressNavigationContext progressContext)
    {
        _viewModel = viewModel;
        _navigation = navigation;
        _progressContext = progressContext;
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

    // Кнопка «Добавить прогресс» в шапке деталей: запоминаем уровень и переходим на вкладку
    // «Прогрессы». DataContext кнопки — LevelDetailViewModel выбранного уровня.
    private void OnOpenProgressesClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LevelDetailViewModel { Level: { } level } })
        {
            _progressContext.TargetLevelId = level.Id;
            _navigation.Navigate(typeof(ProgressesPage));
        }
    }
}
