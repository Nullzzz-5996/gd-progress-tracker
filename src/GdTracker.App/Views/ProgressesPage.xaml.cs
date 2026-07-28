using System.Windows.Controls;
using GdTracker.ViewModels;

namespace GdTracker.App.Views;

public partial class ProgressesPage : Page
{
    private readonly ProgressesViewModel _viewModel;

    public ProgressesPage(ProgressesViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Loaded += async (_, _) => await _viewModel.LoadAsync();
    }

    // Смена уровня пользователем: перезагрузить строки. Программную установку уровня из
    // LoadAsync пропускаем — там строки грузятся сами (IsLoadingLevels), без гонки.
    private async void OnLevelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel.IsLoadingLevels)
            return;
        await _viewModel.LoadRowsAsync();
    }

    // Значения ячеек уже в строке-VM (UpdateSourceTrigger=PropertyChanged), поэтому по
    // завершении редактирования строки достаточно сохранить её.
    private async void OnRowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
    {
        if (e.Row.Item is LevelProgressRowViewModel row)
            await _viewModel.SaveRowAsync(row);
    }
}
