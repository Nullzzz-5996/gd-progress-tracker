using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Navigation;
using GdTracker.ViewModels;

namespace GdTracker.App.Views;

public partial class DemonListPage : Page
{
    private readonly DemonListViewModel _viewModel;

    public DemonListPage(DemonListViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        Loaded += async (_, _) => await _viewModel.LoadAsync();
        // Страница транзитная: без отписки вью-модель осталась бы висеть
        // подписчиком долгоживущего сервиса аккаунта при каждом её открытии.
        Unloaded += (_, _) => _viewModel.Detach();
    }

    /// <summary>
    /// Ссылки на видео открываются во внешнем браузере: показывать ролики внутри
    /// приложения незачем, а без явного <c>UseShellExecute</c> запуск ссылки в .NET
    /// не работает вовсе.
    /// </summary>
    private void OnOpenLink(object sender, RequestNavigateEventArgs e)
    {
        e.Handled = true;

        var url = e.Uri?.AbsoluteUri;
        if (url is null || (e.Uri!.Scheme != Uri.UriSchemeHttp && e.Uri.Scheme != Uri.UriSchemeHttps))
            return;

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Нет браузера по умолчанию или система отказалась открывать ссылку —
            // это не повод ронять вкладку.
        }
    }
}
