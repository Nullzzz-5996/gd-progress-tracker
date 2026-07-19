using System.Windows.Controls;
using GdTracker.ViewModels;

namespace GdTracker.App.Views;

public partial class StatsPage : Page
{
    private readonly StatsViewModel _viewModel;

    public StatsPage(StatsViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Loaded += async (_, _) => await _viewModel.LoadAsync();
        // Страница транзиентная и пересоздаётся при каждом заходе на вкладку — без явной
        // отписки здесь вью-модель оставалась бы подписанной на общую (singleton) палитру
        // графиков и копилась бы в памяти с каждым визитом на вкладку.
        Unloaded += (_, _) => _viewModel.Dispose();
    }
}
