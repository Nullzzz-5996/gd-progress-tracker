using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GdTracker.Cloud;
using GdTracker.Sharing.Cloud;

namespace GdTracker.ViewModels;

/// <summary>
/// Вкладка «Демонлист»: то же, что страница демонлиста на сайте, но в приложении.
/// Данные общие — вкладка ходит в те же эндпоинты того же сервера, поэтому
/// расстановка, мнения и заявки здесь и на сайте всегда одни и те же.
///
/// Читается список без аккаунта: вход нужен только чтобы высказаться о месте
/// демона и подать заявку на рекорд. Когда сервер недоступен, вкладка показывает
/// последнюю сохранённую расстановку и честно об этом пишет.
/// </summary>
public partial class DemonListViewModel : ViewModelBase
{
    /// <summary>
    /// Разделы списка. Границы те же, что на странице сайта: правило чтения
    /// списка, а не свойство записей, поэтому живёт в клиенте, а не в базе.
    /// </summary>
    private static readonly DemonTier[] Tiers =
    {
        new(1, 75, "Основной список", "Места 1–75: рекорды идут в общий зачёт."),
        new(76, 150, "Расширенный список", "Места 76–150: рекорды принимаются, в общий зачёт не идут."),
        new(151, int.MaxValue, "Легаси", "Места 151 и ниже: список ведётся как архив."),
    };

    private readonly ICommunityService _community;
    private readonly ICloudAccountService _account;
    private readonly IConfirmationService _confirmation;

    /// <summary>Какой демон раскрыт: раскрытым держится один, как на сайте.</summary>
    private string? _openDemonId;

    public DemonListViewModel(
        ICommunityService community,
        ICloudAccountService account,
        IConfirmationService confirmation)
    {
        _community = community;
        _account = account;
        _confirmation = confirmation;

        _account.StateChanged += OnAccountStateChanged;
    }

    /// <summary>Отписка от событий сервиса аккаунта: страница живёт меньше, чем он.</summary>
    public void Detach() => _account.StateChanged -= OnAccountStateChanged;

    private void OnAccountStateChanged(object? sender, EventArgs e) => RefreshAccountState();

    public ObservableCollection<DemonRowViewModel> Demons { get; } = new();

    /// <summary>Топ пройденных уровней: одобренные заявки в порядке мест.</summary>
    public ObservableCollection<RecordRowViewModel> TopRecords { get; } = new();

    /// <summary>Очередь на рассмотрение — только у модератора и выше.</summary>
    public ObservableCollection<RecordRowViewModel> PendingRecords { get; } = new();

    /// <summary>Свои заявки вместе с отклонёнными: их видит только автор.</summary>
    public ObservableCollection<RecordRowViewModel> MyRecords { get; } = new();

    /// <summary>Участники — только у владельца сервера.</summary>
    public ObservableCollection<ManagedUserRowViewModel> Users { get; } = new();

    // ------------------------------------------------------------ состояние вкладки

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isBusy;

    /// <summary>Обратное к <see cref="IsBusy"/>: по нему XAML гасит кнопки на время запроса.</summary>
    public bool IsIdle => !IsBusy;

    [ObservableProperty] private string? _status;

    [ObservableProperty] private string? _error;

    /// <summary>
    /// Список показан из локальной копии: сервер не ответил. Всё, что меняет
    /// данные, в этом состоянии недоступно — менять нечего и негде.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOnline))]
    [NotifyPropertyChangedFor(nameof(CanParticipate))]
    [NotifyPropertyChangedFor(nameof(CanModerate))]
    [NotifyPropertyChangedFor(nameof(CanBan))]
    private bool _isOffline;

    public bool IsOnline => !IsOffline;

    /// <summary>Подпись над списком, когда он поднят из локальной копии.</summary>
    [ObservableProperty] private string? _offlineNotice;

    /// <summary>Права вошедшего пользователя в демонлисте. Null — вход не выполнен.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanModerate))]
    [NotifyPropertyChangedFor(nameof(CanBan))]
    [NotifyPropertyChangedFor(nameof(CanParticipate))]
    [NotifyPropertyChangedFor(nameof(IsListBanned))]
    [NotifyPropertyChangedFor(nameof(ListBanNotice))]
    private CommunityProfile? _profile;

    public bool IsSignedIn => _account.IsSignedIn;

    public bool IsSignedOut => !IsSignedIn;

    /// <summary>Адрес сервера — показывается рядом с подписью о доступности.</summary>
    public string ServerUrl => _account.ServerUrl;

    /// <summary>Двигает демонов и рассматривает заявки: модератор, администратор, владелец.</summary>
    public bool CanModerate => IsOnline && Profile is { CanModerate: true };

    /// <summary>Банит аккаунты: только владелец сервера.</summary>
    public bool CanBan => IsOnline && Profile is { CanBan: true };

    /// <summary>Забанен в демонлисте: список читается, участвовать нельзя.</summary>
    public bool IsListBanned => Profile is { ListBanned: true };

    /// <summary>Может писать мнения и подавать заявки: вошёл, не забанен, сервер на связи.</summary>
    public bool CanParticipate => IsOnline && Profile is { ListBanned: false };

    public string? ListBanNotice => Profile is { ListBanned: true } profile
        ? string.IsNullOrWhiteSpace(profile.ListBanReason)
            ? "Ты забанен в демонлисте: список читается, участвовать нельзя."
            : $"Ты забанен в демонлисте: {profile.ListBanReason}"
        : null;

    /// <summary>Сколько демонов в списке — верхняя граница для полей с местом.</summary>
    public int DemonCount => Demons.Count;

    // ------------------------------------------------------------------- загрузка

    /// <summary>
    /// Загружает вкладку при открытии. Ошибки дальше строки состояния не идут:
    /// метод вызывается из обработчика Loaded.
    /// </summary>
    public async Task LoadAsync()
    {
        // Повторная подписка на случай, если страницу уже открывали и покидали:
        // навигация вызывает Detach, а при возврате WPF-UI может показать тот же экземпляр.
        _account.StateChanged -= OnAccountStateChanged;
        _account.StateChanged += OnAccountStateChanged;

        RefreshAccountState();
        await RefreshAsync();
    }

    [RelayCommand]
    private Task RefreshAsync() => RunAsync("Загрузка демонлиста...", async () =>
    {
        var snapshot = await _community.GetDemonsAsync();
        FillDemons(snapshot.Demons);

        IsOffline = snapshot.FromCache;
        OfflineNotice = snapshot.CachedAtUtc is { } cached
            ? $"Сервер {ServerUrl} не отвечает. Показана сохранённая расстановка от "
              + $"{cached.ToLocalTime():dd.MM.yyyy HH:mm} — только для чтения."
            : null;

        if (IsOffline)
        {
            // Без сервера нет ни прав, ни заявок, ни участников: чтобы вкладка
            // не показывала вчерашние кнопки, всё это очищается.
            Profile = null;
            TopRecords.Clear();
            PendingRecords.Clear();
            MyRecords.Clear();
            Users.Clear();
            Status = null;
            return;
        }

        Fill(TopRecords, await _community.GetTopRecordsAsync());
        await LoadSignedInPartsAsync();
        await ReopenReloadedDemonAsync();

        Status = $"В списке {Demons.Count} демонов, в топе пройденных — {TopRecords.Count}.";
    });

    /// <summary>
    /// Части вкладки, которые есть только у вошедшего пользователя. Отказ по токену
    /// здесь не должен рушить всю загрузку: список и топ уже прочитаны и остаются
    /// на экране, а пользователь видит, что нужно войти заново.
    /// </summary>
    private async Task LoadSignedInPartsAsync()
    {
        if (!_account.IsSignedIn)
        {
            Profile = null;
            PendingRecords.Clear();
            MyRecords.Clear();
            Users.Clear();
            return;
        }

        try
        {
            Profile = await _community.GetProfileAsync();
            Fill(MyRecords, await _community.GetMyRecordsAsync());

            if (Profile.CanModerate)
                Fill(PendingRecords, await _community.GetPendingRecordsAsync());
            else
                PendingRecords.Clear();

            if (Profile.CanBan)
                Fill(Users, await _community.GetUsersAsync(UserQuery), user => new ManagedUserRowViewModel(this, user));
            else
                Users.Clear();
        }
        catch (CloudException e) when (e.Kind == CloudErrorKind.Unauthorized)
        {
            Profile = null;
            PendingRecords.Clear();
            MyRecords.Clear();
            Users.Clear();
            Error = e.Message;
        }
    }

    private void FillDemons(IReadOnlyList<DemonView> demons)
    {
        Demons.Clear();
        foreach (var demon in demons.OrderBy(d => d.Position))
            Demons.Add(new DemonRowViewModel(this, demon, TierOf(demon.Position), demons.Count));

        OnPropertyChanged(nameof(DemonCount));
    }

    /// <summary>
    /// После перезагрузки списка строки создаются заново, поэтому раскрытый демон
    /// раскрывается ещё раз: иначе перестановка или новое мнение закрывали бы
    /// карточку, с которой пользователь работает.
    /// </summary>
    private async Task ReopenReloadedDemonAsync()
    {
        if (_openDemonId is null)
            return;

        var row = Demons.FirstOrDefault(d => d.Id == _openDemonId);
        if (row is null)
        {
            _openDemonId = null;
            return;
        }

        row.IsExpanded = true;
        await LoadOpinionsAsync(row);
    }

    private static DemonTier TierOf(int position)
        => Tiers.FirstOrDefault(t => position >= t.From && position <= t.To) ?? Tiers[^1];

    // --------------------------------------------------------------------- мнения

    /// <summary>Раскрывает строку демона и подтягивает мнения о его месте.</summary>
    [RelayCommand]
    private async Task ToggleDemonAsync(DemonRowViewModel? row)
    {
        if (row is null)
            return;

        if (row.IsExpanded)
        {
            row.IsExpanded = false;
            _openDemonId = null;
            return;
        }

        foreach (var other in Demons.Where(d => d.IsExpanded))
            other.IsExpanded = false;

        row.IsExpanded = true;
        _openDemonId = row.Id;

        if (IsOnline)
            await LoadOpinionsAsync(row);
    }

    private async Task LoadOpinionsAsync(DemonRowViewModel row)
    {
        row.OpinionsLoading = true;
        try
        {
            var opinions = await _community.GetOpinionsAsync(row.Id);

            row.Opinions.Clear();
            foreach (var opinion in opinions)
                row.Opinions.Add(new OpinionRowViewModel(this, row, opinion, CanRemoveOpinion(opinion)));

            row.OpinionCount = opinions.Count;
            row.HasNoOpinions = opinions.Count == 0;
        }
        catch (CloudException e)
        {
            Error = e.Message;
        }
        finally
        {
            row.OpinionsLoading = false;
        }
    }

    /// <summary>Своё мнение убирает автор, чужое — модератор.</summary>
    private bool CanRemoveOpinion(OpinionView opinion)
        => Profile is { } profile && (profile.UserId == opinion.AuthorId || profile.CanModerate);

    [RelayCommand]
    private Task PostOpinionAsync(DemonRowViewModel? row)
    {
        if (row is null)
            return Task.CompletedTask;

        var text = row.NewOpinionText.Trim();
        if (text.Length < 3)
        {
            Error = "Мнение короче трёх символов сервер не примет.";
            return Task.CompletedTask;
        }

        if (!TryParseOptionalPosition(row.NewOpinionPosition, out var suggested))
            return Task.CompletedTask;

        return RunAsync("Отправка мнения...", async () =>
        {
            await _community.PostOpinionAsync(row.Id, text, suggested);
            row.NewOpinionText = string.Empty;
            row.NewOpinionPosition = string.Empty;
            await LoadOpinionsAsync(row);
            Status = "Мнение опубликовано.";
        });
    }

    [RelayCommand]
    private Task DeleteOpinionAsync(OpinionRowViewModel? opinion)
    {
        if (opinion is null)
            return Task.CompletedTask;

        if (!_confirmation.Confirm("Удаление мнения", "Удалить это мнение о размещении?"))
            return Task.CompletedTask;

        return RunAsync("Удаление мнения...", async () =>
        {
            await _community.DeleteOpinionAsync(opinion.Id);
            await LoadOpinionsAsync(opinion.Demon);
            Status = "Мнение удалено.";
        });
    }

    // ---------------------------------------------------------- правка списка

    /// <summary>Перестановка демона: доступна модератору и выше.</summary>
    [RelayCommand]
    private Task MoveDemonAsync(DemonRowViewModel? row)
    {
        if (row is null)
            return Task.CompletedTask;

        if (!TryParsePosition(row.MovePosition, Demons.Count, out var position))
            return Task.CompletedTask;

        return MoveToAsync(row, position);
    }

    /// <summary>Шаг вверх: то же, что стрелка «↑» в строке на сайте.</summary>
    [RelayCommand]
    private Task MoveDemonUpAsync(DemonRowViewModel? row)
        => row is { CanMoveUp: true } ? MoveToAsync(row, row.Position - 1) : Task.CompletedTask;

    /// <summary>Шаг вниз: стрелка «↓».</summary>
    [RelayCommand]
    private Task MoveDemonDownAsync(DemonRowViewModel? row)
        => row is { CanMoveDown: true } ? MoveToAsync(row, row.Position + 1) : Task.CompletedTask;

    private Task MoveToAsync(DemonRowViewModel row, int position) =>
        RunAsync("Перестановка демона...", async () =>
        {
            await _community.MoveDemonAsync(row.Id, position);
            await ReloadListAsync();
            Status = $"«{row.Name}» теперь на месте {position}.";
        });

    [RelayCommand]
    private Task DeleteDemonAsync(DemonRowViewModel? row)
    {
        if (row is null)
            return Task.CompletedTask;

        if (!_confirmation.Confirm(
                "Удаление из списка",
                $"Убрать «{row.Name}» из демонлиста? Все, кто стоит ниже, поднимутся на место вверх."))
        {
            return Task.CompletedTask;
        }

        return RunAsync("Удаление демона...", async () =>
        {
            if (_openDemonId == row.Id)
                _openDemonId = null;

            await _community.DeleteDemonAsync(row.Id);
            await ReloadListAsync();
            Status = $"«{row.Name}» убран из списка.";
        });
    }

    /// <summary>Место, на которое встаёт новый демон. Пусто — в конец списка.</summary>
    [ObservableProperty] private string _newDemonPosition = string.Empty;

    [ObservableProperty] private string _newDemonName = string.Empty;

    [ObservableProperty] private string _newDemonPublisher = string.Empty;

    [ObservableProperty] private string _newDemonVerifier = string.Empty;

    [ObservableProperty] private string _newDemonLevelId = string.Empty;

    [ObservableProperty] private string _newDemonVideo = string.Empty;

    /// <summary>Минимальный процент для рекорда. Пусто — сервер поставит 100.</summary>
    [ObservableProperty] private string _newDemonRequirement = string.Empty;

    [RelayCommand]
    private Task AddDemonAsync()
    {
        var name = NewDemonName.Trim();
        if (name.Length == 0)
        {
            Error = "Укажи название уровня.";
            return Task.CompletedTask;
        }

        // Новый демон может встать и в самый конец, поэтому верхняя граница на единицу
        // больше длины списка. Пустое поле означает «в конец».
        var last = Demons.Count + 1;
        var position = last;
        if (NewDemonPosition.Trim().Length > 0 && !TryParsePosition(NewDemonPosition, last, out position))
            return Task.CompletedTask;

        long? levelId = null;
        if (NewDemonLevelId.Trim().Length > 0)
        {
            if (!long.TryParse(NewDemonLevelId.Trim(), out var parsed) || parsed <= 0)
            {
                Error = "Идентификатор уровня — целое положительное число.";
                return Task.CompletedTask;
            }

            levelId = parsed;
        }

        int? requirement = null;
        if (NewDemonRequirement.Trim().Length > 0)
        {
            if (!int.TryParse(NewDemonRequirement.Trim(), out var parsed) || parsed is < 1 or > 100)
            {
                Error = "Требование — процент от 1 до 100.";
                return Task.CompletedTask;
            }

            requirement = parsed;
        }

        var request = new AddDemonRequest(
            position,
            name,
            Trimmed(NewDemonPublisher),
            Trimmed(NewDemonVerifier),
            levelId,
            Trimmed(NewDemonVideo),
            Thumbnail: null,
            requirement);

        return RunAsync("Добавление демона...", async () =>
        {
            await _community.AddDemonAsync(request);

            NewDemonPosition = string.Empty;
            NewDemonName = string.Empty;
            NewDemonPublisher = string.Empty;
            NewDemonVerifier = string.Empty;
            NewDemonLevelId = string.Empty;
            NewDemonVideo = string.Empty;
            NewDemonRequirement = string.Empty;

            await ReloadListAsync();
            Status = $"«{name}» добавлен на место {position}.";
        });
    }

    // ------------------------------------------------- топ пройденных уровней

    /// <summary>Демон из списка, на который подаётся заявка. Null — произвольный уровень.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsLevelName))]
    private DemonRowViewModel? _selectedRecordDemon;

    /// <summary>Название уровня для заявки не из списка.</summary>
    [ObservableProperty] private string _recordLevelName = string.Empty;

    /// <summary>Нужно ли вводить название вручную: у демона из списка оно уже есть.</summary>
    public bool NeedsLevelName => SelectedRecordDemon is null;

    [ObservableProperty] private string _recordProgress = "100";

    [ObservableProperty] private string _recordVideoUrl = string.Empty;

    /// <summary>Заявка принимается только с видимыми кликами — это требование сервера.</summary>
    [ObservableProperty] private bool _recordHasClicks;

    [ObservableProperty] private string _recordComment = string.Empty;

    [RelayCommand]
    private Task SubmitRecordAsync()
    {
        var video = RecordVideoUrl.Trim();
        if (!video.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !video.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            Error = "Нужна ссылка на видео прохождения (http или https).";
            return Task.CompletedTask;
        }

        if (!RecordHasClicks)
        {
            Error = "Заявка принимается только с видео, на котором слышны или видны клики.";
            return Task.CompletedTask;
        }

        if (!int.TryParse(RecordProgress.Trim(), out var progress) || progress is < 1 or > 100)
        {
            Error = "Процент прохождения — число от 1 до 100.";
            return Task.CompletedTask;
        }

        var levelName = SelectedRecordDemon?.Name ?? RecordLevelName.Trim();
        if (levelName.Length == 0)
        {
            Error = "Выбери демона из списка или укажи название уровня.";
            return Task.CompletedTask;
        }

        var request = new SubmitRecordRequest(
            SelectedRecordDemon?.Id,
            levelName,
            progress,
            video,
            HasClicks: true,
            Trimmed(RecordComment));

        return RunAsync("Отправка заявки...", async () =>
        {
            await _community.SubmitRecordAsync(request);

            RecordVideoUrl = string.Empty;
            RecordComment = string.Empty;
            RecordHasClicks = false;

            await ReloadRecordsAsync();
            Status = "Заявка отправлена — её посмотрит модератор.";
        });
    }

    [RelayCommand]
    private Task ApproveRecordAsync(RecordRowViewModel? record) => ReviewRecordAsync(record, approve: true);

    [RelayCommand]
    private Task RejectRecordAsync(RecordRowViewModel? record) => ReviewRecordAsync(record, approve: false);

    private Task ReviewRecordAsync(RecordRowViewModel? record, bool approve)
    {
        if (record is null)
            return Task.CompletedTask;

        int? placement = null;
        if (approve && record.ReviewPlacement.Trim().Length > 0)
        {
            // Место в топе, а не в демонлисте: верхняя граница — размер топа плюс один.
            if (!TryParsePosition(record.ReviewPlacement, TopRecords.Count + 1, out var parsed))
                return Task.CompletedTask;

            placement = parsed;
        }

        var request = new ReviewRecordRequest(approve, placement, Trimmed(record.ReviewNoteInput));

        return RunAsync(approve ? "Одобрение заявки..." : "Отклонение заявки...", async () =>
        {
            await _community.ReviewRecordAsync(record.Id, request);
            await ReloadRecordsAsync();
            Status = approve
                ? $"Заявка «{record.LevelName}» одобрена."
                : $"Заявка «{record.LevelName}» отклонена.";
        });
    }

    // --------------------------------------------------------------- участники

    /// <summary>Поиск по нику и почте: список участников растёт.</summary>
    [ObservableProperty] private string _userQuery = string.Empty;

    [RelayCommand]
    private Task SearchUsersAsync() => RunAsync("Поиск участников...", async () =>
    {
        Fill(Users, await _community.GetUsersAsync(UserQuery), user => new ManagedUserRowViewModel(this, user));
        Status = Users.Count == 0 ? "Никого не нашлось." : $"Найдено участников: {Users.Count}.";
    });

    [RelayCommand]
    private Task ToggleBanAsync(ManagedUserRowViewModel? user)
    {
        if (user is null)
            return Task.CompletedTask;

        var banned = !user.Banned;
        if (banned && !_confirmation.Confirm(
                "Бан аккаунта",
                $"Забанить {user.Username}? Аккаунт потеряет и вход, и синхронизацию."))
        {
            return Task.CompletedTask;
        }

        return RunAsync(banned ? "Бан аккаунта..." : "Снятие бана...", async () =>
        {
            user.Apply(await _community.SetBanAsync(user.Id, banned, Trimmed(user.ReasonInput)));
            Status = banned ? $"{user.Username} забанен." : $"Бан с {user.Username} снят.";
        });
    }

    [RelayCommand]
    private Task ToggleListBanAsync(ManagedUserRowViewModel? user)
    {
        if (user is null)
            return Task.CompletedTask;

        var banned = !user.ListBanned;

        return RunAsync(banned ? "Бан в демонлисте..." : "Снятие бана в демонлисте...", async () =>
        {
            user.Apply(await _community.SetListBanAsync(user.Id, banned, Trimmed(user.ReasonInput)));
            Status = banned
                ? $"{user.Username} убран из демонлиста."
                : $"{user.Username} вернулся в демонлист.";
        });
    }

    // ------------------------------------------------------------------ обвязка

    /// <summary>Перечитывает список после правки: места сдвигаются у всех сразу.</summary>
    private async Task ReloadListAsync()
    {
        var snapshot = await _community.GetDemonsAsync();
        FillDemons(snapshot.Demons);
        await ReopenReloadedDemonAsync();
    }

    /// <summary>Перечитывает топ, свои заявки и очередь — одобрение меняет все три.</summary>
    private async Task ReloadRecordsAsync()
    {
        Fill(TopRecords, await _community.GetTopRecordsAsync());

        if (Profile is null)
            return;

        Fill(MyRecords, await _community.GetMyRecordsAsync());

        if (Profile.CanModerate)
            Fill(PendingRecords, await _community.GetPendingRecordsAsync());
    }

    private void Fill(ObservableCollection<RecordRowViewModel> target, IReadOnlyList<RecordView> records)
        => Fill(target, records, record => new RecordRowViewModel(this, record));

    private static void Fill<TSource, TRow>(
        ObservableCollection<TRow> target, IReadOnlyList<TSource> source, Func<TSource, TRow> create)
    {
        target.Clear();
        foreach (var item in source)
            target.Add(create(item));
    }

    private static string? Trimmed(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Разбор обязательного номера места с проверкой границ.</summary>
    private bool TryParsePosition(string text, int max, out int position)
    {
        if (!int.TryParse(text.Trim(), out position) || position < 1 || position > max)
        {
            Error = $"Место — число от 1 до {max}.";
            position = 0;
            return false;
        }

        return true;
    }

    /// <summary>Разбор необязательного номера места: пустая строка — законное значение.</summary>
    private bool TryParseOptionalPosition(string text, out int? position)
    {
        position = null;
        if (text.Trim().Length == 0)
            return true;

        if (!TryParsePosition(text, Demons.Count, out var parsed))
            return false;

        position = parsed;
        return true;
    }

    /// <summary>
    /// Общая обвязка команд: занятость, очистка прошлой ошибки и превращение
    /// сбоев сервера в сообщение на вкладке. Демонлист необязателен ровно так же,
    /// как аккаунт, поэтому ни один его сбой не всплывает исключением.
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
            Error = e.Kind == CloudErrorKind.Network
                ? $"{e.Message} Проверь адрес сервера на вкладке «Аккаунт» и подключение к сети."
                : e.Message;
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

    private void RefreshAccountState()
    {
        OnPropertyChanged(nameof(IsSignedIn));
        OnPropertyChanged(nameof(IsSignedOut));
        OnPropertyChanged(nameof(ServerUrl));
    }
}
