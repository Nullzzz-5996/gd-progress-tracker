using GdTracker.App.Views;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace GdTracker.App;

/// <summary>
/// Главное окно: оболочка с навигацией WPF UI.
/// Страницы резолвятся из DI через <see cref="NavigationView.SetServiceProvider"/>.
/// </summary>
public partial class MainWindow : FluentWindow
{
    private readonly INavigationService _navigationService;

    public MainWindow(INavigationService navigationService, IServiceProvider serviceProvider)
    {
        _navigationService = navigationService;

        InitializeComponent();

        RootNavigation.SetServiceProvider(serviceProvider);
        _navigationService.SetNavigationControl(RootNavigation);

        Loaded += (_, _) => _navigationService.Navigate(typeof(DashboardPage));
    }
}
