using System;
using System.Windows.Controls;
using System.Windows.Threading;
using GdTracker.ViewModels;

namespace GdTracker.App.Views;

public partial class CpsPage : Page
{
    private readonly CpsViewModel _viewModel;
    private readonly DispatcherTimer _timer;

    public CpsPage(CpsViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        // Живой показ во время замера; RefreshLive сам ничего не делает вне замера.
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += (_, _) => _viewModel.RefreshLive();
        _timer.Start();

        // Уходим со страницы — остановить таймер и утилизировать VM: Dispose останавливает
        // замер (снимает хуки, если шёл) и отписывается от событий монитора (как StatsPage).
        Unloaded += (_, _) =>
        {
            _timer.Stop();
            _viewModel.Dispose();
        };
    }
}
