using Wpf.Ui.Abstractions;

namespace GdTracker.App.Services;

/// <summary>
/// Провайдер страниц WPF UI на основе DI-контейнера: страницы резолвятся
/// из <see cref="IServiceProvider"/> по их типу (требуется WPF UI NavigationService).
/// </summary>
public class PageProvider : INavigationViewPageProvider
{
    private readonly IServiceProvider _serviceProvider;

    public PageProvider(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public object? GetPage(Type pageType) => _serviceProvider.GetService(pageType);
}
