using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GdTracker.Cloud;
using GdTracker.Sharing.Cloud;

namespace GdTracker.ViewModels;

/// <summary>
/// Страница «Аккаунт»: вход, регистрация и синхронизация прогресса с облаком.
/// Аккаунт необязателен — без входа приложение ведёт себя ровно так же, как раньше,
/// а эта страница просто предлагает войти.
/// </summary>
public partial class AccountViewModel : ViewModelBase
{
    private readonly ICloudAccountService _account;
    private readonly ICloudSyncService _sync;
    private readonly ICloudSettingsStore _settings;
    private readonly IConfirmationService _confirmation;

    public AccountViewModel(
        ICloudAccountService account,
        ICloudSyncService sync,
        ICloudSettingsStore settings,
        IConfirmationService confirmation)
    {
        _account = account;
        _sync = sync;
        _settings = settings;
        _confirmation = confirmation;

        // Присваиваем полям напрямую: через свойства сработали бы обработчики
        // OnXxxChanged и переписали бы настройки теми же значениями при каждом
        // открытии страницы.
        _serverUrl = settings.ServerUrl;
        _autoSyncOnStartup = settings.AutoSyncOnStartup;
        _email = settings.LastEmail ?? string.Empty;

        _account.StateChanged += OnAccountStateChanged;
    }

    /// <summary>Отписка от событий сервиса аккаунта: страница живёт меньше, чем он.</summary>
    public void Detach() => _account.StateChanged -= OnAccountStateChanged;

    private void OnAccountStateChanged(object? sender, EventArgs e) => RefreshAccountState();

    public bool IsSignedIn => _account.IsSignedIn;

    /// <summary>Почта вошедшего пользователя (для панели «вошли как…»).</summary>
    public string AccountEmail => _account.Email ?? string.Empty;

    /// <summary>Ник вошедшего пользователя — имя, под которым он виден в демонлисте.</summary>
    public string AccountUsername => _account.Username ?? string.Empty;

    /// <summary>Роль рядом с ником: подпись на кнопке-значке в интерфейсе.</summary>
    public string AccountRoleLabel => RoleLabel(_account.Role);

    /// <summary>
    /// Роль выше обычного участника подсвечивается: у модератора, администратора
    /// и владельца значок должен быть заметен, у рядового участника — нет.
    /// </summary>
    public bool IsPrivilegedRole => _account.Role >= UserRole.Moderator;

    /// <summary>Обратное к <see cref="IsSignedIn"/>: XAML показывает по нему форму входа.</summary>
    public bool IsSignedOut => !IsSignedIn;

    /// <summary>Русская подпись роли.</summary>
    public static string RoleLabel(UserRole? role) => role switch
    {
        UserRole.Owner => "владелец",
        UserRole.Administrator => "администратор",
        UserRole.Moderator => "модератор",
        UserRole.Member => "участник",
        _ => string.Empty,
    };

    [ObservableProperty] private string _serverUrl;

    partial void OnServerUrlChanged(string value) => _account.ServerUrl = value;

    [ObservableProperty] private string _email;

    /// <summary>
    /// Ник для регистрации и для смены имени. Виден всем в демонлисте, поэтому
    /// уникален: занятое имя сервер отклонит.
    /// </summary>
    [ObservableProperty] private string _username = string.Empty;

    /// <summary>
    /// Пароль. Заполняется из PasswordBox кодом страницы: WPF намеренно не даёт
    /// привязывать Password напрямую, чтобы пароль не оседал в дереве привязок.
    /// </summary>
    [ObservableProperty] private string _password = string.Empty;

    [ObservableProperty] private bool _autoSyncOnStartup;

    partial void OnAutoSyncOnStartupChanged(bool value) => _settings.AutoSyncOnStartup = value;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isBusy;

    /// <summary>Обратное к <see cref="IsBusy"/>: по нему XAML гасит кнопки на время запроса.</summary>
    public bool IsIdle => !IsBusy;

    [ObservableProperty] private string? _status;

    [ObservableProperty] private string? _error;

    /// <summary>Когда прогресс последний раз уезжал в облако или приезжал оттуда.</summary>
    public string LastSyncText => _settings.LastSyncAtUtc is { } utc
        ? $"Последняя синхронизация: {utc.ToLocalTime():dd.MM.yyyy HH:mm}"
        : "Синхронизации ещё не было";

    /// <summary>
    /// Подтягивает состояние при открытии страницы. Ошибки сети сюда не пускаются
    /// дальше строки статуса: страница вызывается из обработчика Loaded.
    /// </summary>
    public async Task LoadAsync()
    {
        // Повторная подписка на случай, если страницу уже открывали и покидали:
        // навигация вызывает Detach, а при возврате WPF-UI может показать тот же экземпляр.
        _account.StateChanged -= OnAccountStateChanged;
        _account.StateChanged += OnAccountStateChanged;

        RefreshAccountState();

        if (!_account.IsSignedIn)
            return;

        try
        {
            var info = await _account.GetAccountInfoAsync();
            Status = info.SnapshotUpdatedAtUtc is { } updated
                ? $"В облаке есть снимок от {updated.ToLocalTime():dd.MM.yyyy HH:mm} (ревизия {info.SnapshotRevision})."
                : "В облаке пока пусто — отправь туда свой прогресс.";
        }
        catch (CloudException e)
        {
            Status = null;
            Error = e.Kind == CloudErrorKind.Network ? $"{e.Message} {ServerHint}" : e.Message;
        }
    }

    [RelayCommand]
    private async Task SignInAsync()
    {
        if (!ValidateSignIn())
            return;

        await RunAsync("Вход...", async () =>
        {
            await _account.SignInAsync(SignInLogin, Password);
            Password = string.Empty;
            Status = $"Вошли как {_account.Username}.";
        });
    }

    [RelayCommand]
    private async Task RegisterAsync()
    {
        if (!ValidateRegistration())
            return;

        if (string.IsNullOrWhiteSpace(Username))
        {
            Error = "Придумай ник — под ним тебя увидят в демонлисте.";
            return;
        }

        await RunAsync("Регистрация...", async () =>
        {
            await _account.RegisterAsync(Email, Password, Username);
            Password = string.Empty;
            Status = $"Аккаунт {_account.Username} создан. Прогресс можно отправить в облако.";
        });
    }

    /// <summary>Смена ника у вошедшего пользователя.</summary>
    [RelayCommand]
    private Task ChangeUsernameAsync()
    {
        if (string.IsNullOrWhiteSpace(Username))
        {
            Error = "Введи новый ник в поле выше.";
            return Task.CompletedTask;
        }

        return RunAsync("Смена ника...", async () =>
        {
            await _account.ChangeUsernameAsync(Username);
            Status = $"Теперь ты — {_account.Username}.";
        });
    }

    [RelayCommand]
    private void SignOut()
    {
        _account.SignOut();
        Status = "Вышли из аккаунта. Локальный прогресс остался на месте.";
        Error = null;
    }

    [RelayCommand]
    private Task SyncAsync() => RunAsync("Синхронизация...", async () =>
    {
        var outcome = await _sync.SyncAsync();
        Status = DescribeSync(outcome);
    });

    [RelayCommand]
    private Task PullAsync() => RunAsync("Загрузка из облака...", async () =>
    {
        var outcome = await _sync.PullAsync();
        Status = DescribeSync(outcome);
    });

    [RelayCommand]
    private Task PushAsync() => RunAsync("Отправка в облако...", async () =>
    {
        var outcome = await _sync.PushAsync();
        Status = $"Прогресс отправлен в облако (ревизия {outcome.Revision}).";
    });

    [RelayCommand]
    private Task DeleteCloudDataAsync()
    {
        if (!_confirmation.Confirm(
                "Удаление данных из облака",
                "Удалить облачную копию прогресса? Локальные данные останутся на компьютере."))
        {
            return Task.CompletedTask;
        }

        return RunAsync("Удаление данных из облака...", async () =>
        {
            await _account.DeleteCloudSnapshotAsync();
            _settings.LastRevision = 0;
            Status = "Облачная копия удалена. Локальный прогресс не тронут.";
        });
    }

    [RelayCommand]
    private Task DeleteAccountAsync()
    {
        if (string.IsNullOrEmpty(Password))
        {
            Error = "Введи пароль в поле выше — он подтверждает удаление аккаунта.";
            return Task.CompletedTask;
        }

        if (!_confirmation.Confirm(
                "Удаление аккаунта",
                "Удалить аккаунт вместе с облачной копией прогресса? Локальные данные останутся на компьютере."))
        {
            return Task.CompletedTask;
        }

        return RunAsync("Удаление аккаунта...", async () =>
        {
            await _account.DeleteAccountAsync(Password);
            Password = string.Empty;
            Status = "Аккаунт удалён. Приложение продолжает работать локально.";
        });
    }

    /// <summary>
    /// Чем входим: ником, а если поле ника пустое — почтой. Сервер принимает и то,
    /// и другое, поэтому привычка входить по почте никуда не делась, но помнить
    /// её ради входа больше не нужно.
    /// </summary>
    private string SignInLogin => string.IsNullOrWhiteSpace(Username) ? Email : Username;

    private bool ValidateSignIn()
    {
        if (string.IsNullOrWhiteSpace(SignInLogin))
        {
            Error = "Укажи ник (или почту, если ник забылся).";
            return false;
        }

        return ValidatePassword();
    }

    /// <summary>При регистрации почта обязательна: ник ей не замена.</summary>
    private bool ValidateRegistration()
    {
        if (string.IsNullOrWhiteSpace(Email))
        {
            Error = "Укажи почту.";
            return false;
        }

        return ValidatePassword();
    }

    private bool ValidatePassword()
    {
        if (string.IsNullOrEmpty(Password))
        {
            Error = "Укажи пароль.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Общая обвязка команд: занятость, очистка прошлой ошибки и превращение
    /// облачных сбоев в сообщение на странице. Никакой сбой синхронизации не должен
    /// мешать пользоваться остальным приложением, поэтому исключения не всплывают.
    /// </summary>
    private async Task RunAsync(string busyStatus, Func<Task> action)
    {
        IsBusy = true;
        Error = null;
        Status = busyStatus;

        try
        {
            await action();
        }
        catch (CloudException e)
        {
            Status = null;
            // Недоступный сервер — самая частая причина, по которой «не проходит
            // регистрация», и одного сообщения об отказе соединения мало:
            // подсказываем, что именно нужно сделать.
            Error = e.Kind == CloudErrorKind.Network ? $"{e.Message} {ServerHint}" : e.Message;
        }
        catch (Exception e)
        {
            Status = null;
            Error = $"Не удалось выполнить операцию: {e.Message}";
        }
        finally
        {
            IsBusy = false;
            RefreshAccountState();
        }
    }

    /// <summary>
    /// Что делать, когда сервер не ответил. Для своего компьютера это почти
    /// всегда «сервер не запущен», для чужого адреса — опечатка или сеть.
    /// </summary>
    public string ServerHint => IsLocalServer(ServerUrl)
        ? "Сервер синхронизации на этом компьютере не запущен — запусти его командой "
          + "«powershell -File tools/run-sync-server.ps1» в папке проекта и попробуй снова. "
          + "Аккаунт нужен только для синхронизации: всё остальное работает и без него."
        : "Проверь адрес сервера выше и подключение к сети. "
          + "Аккаунт нужен только для синхронизации: всё остальное работает и без него.";

    /// <summary>Указывает ли адрес на этот же компьютер.</summary>
    private static bool IsLocalServer(string url) =>
        Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri)
        && (uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase));

    /// <summary>Проверка доступности сервера — до всякой регистрации.</summary>
    [RelayCommand]
    private Task CheckServerAsync() => RunAsync("Проверка сервера...", async () =>
    {
        if (await _account.CheckServerAsync())
        {
            Status = $"Сервер {ServerUrl} отвечает — можно регистрироваться и входить.";
            return;
        }

        Status = null;
        Error = $"Сервер {ServerUrl} ответил, но не тем, чего ждёт приложение. {ServerHint}";
    });

    private static string DescribeSync(SyncOutcome outcome)
    {
        var merged = outcome.LevelsAdded + outcome.LevelsUpdated + outcome.RecordsAdded == 0
            ? "новых данных из облака не было"
            : $"из облака добавлено уровней: {outcome.LevelsAdded}, обновлено: {outcome.LevelsUpdated}, записей: {outcome.RecordsAdded}";

        var uploaded = outcome.Uploaded ? ", локальные данные отправлены" : string.Empty;
        return $"Синхронизация завершена: {merged}{uploaded} (ревизия {outcome.Revision}).";
    }

    private void RefreshAccountState()
    {
        OnPropertyChanged(nameof(IsSignedIn));
        OnPropertyChanged(nameof(IsSignedOut));
        OnPropertyChanged(nameof(AccountEmail));
        OnPropertyChanged(nameof(AccountUsername));
        OnPropertyChanged(nameof(AccountRoleLabel));
        OnPropertyChanged(nameof(IsPrivilegedRole));
        OnPropertyChanged(nameof(LastSyncText));
    }
}
