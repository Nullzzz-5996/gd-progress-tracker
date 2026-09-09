using System.Windows.Controls;
using GdTracker.ViewModels;

namespace GdTracker.App.Views;

public partial class AccountPage : Page
{
    private readonly AccountViewModel _viewModel;

    public AccountPage(AccountViewModel viewModel)
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
    /// WPF намеренно не даёт привязывать <see cref="PasswordBox.Password"/> к вью-модели,
    /// поэтому значение переносится вручную. Оба поля страницы (вход и подтверждение
    /// удаления) пишут в одно свойство: одновременно видно только одно из них.
    /// </summary>
    private void OnPasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is PasswordBox box)
            _viewModel.Password = box.Password;
    }
}
