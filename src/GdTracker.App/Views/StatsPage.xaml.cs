using System.Windows.Controls;
using System.Windows.Input;
using GdTracker.ViewModels;

namespace GdTracker.App.Views;

public partial class StatsPage : Page
{
    // Столько же, сколько прокручивает ScrollViewer по умолчанию: три «строки» по 16 px на щелчок колеса.
    private const double WheelScrollStep = 48d;

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

    // Графики LiveCharts поглощают MouseWheel (у них это жест зума) и не дают событию всплыть
    // до ScrollViewer, из-за чего страница не прокручивалась, пока курсор был над графиком.
    // Прокручиваем сами на этапе туннелирования — до того, как график увидит событие.
    // С зажатым Ctrl событие не трогаем: оно дойдёт до графика и сработает зум.
    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            return;
        }

        scrollViewer.ScrollToVerticalOffset(
            scrollViewer.VerticalOffset - (double)e.Delta / Mouse.MouseWheelDeltaForOneLine * WheelScrollStep);
        e.Handled = true;
    }
}
