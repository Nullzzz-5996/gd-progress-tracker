using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GdTracker.Sharing.Cloud;

namespace GdTracker.ViewModels;

/// <summary>
/// Раздел списка: те же три части, что и на странице сайта. Границы заданы здесь
/// и на сервере не хранятся — это способ читать список, а не его свойство.
/// </summary>
/// <param name="From">Первое место раздела.</param>
/// <param name="To">Последнее место раздела.</param>
public sealed record DemonTier(int From, int To, string Title, string Note);

/// <summary>Демон в списке: строка вкладки и её раскрытая часть с мнениями.</summary>
public partial class DemonRowViewModel : ObservableObject
{
    public DemonRowViewModel(DemonListViewModel owner, DemonView demon, DemonTier tier, int listLength)
    {
        Owner = owner;
        Tier = tier;
        ListLength = listLength;
        Id = demon.Id;
        Position = demon.Position;
        Name = demon.Name;
        Publisher = demon.Publisher;
        Verifier = demon.Verifier;
        LevelId = demon.LevelId;
        Video = demon.Video;
        Requirement = demon.Requirement;
        _opinionCount = demon.OpinionCount;
        _movePosition = demon.Position.ToString();
    }

    /// <summary>Вью-модель вкладки: строки берут команды у неё, своих не заводят.</summary>
    public DemonListViewModel Owner { get; }

    /// <summary>Идентификатор записи в списке, а не идентификатор уровня в игре.</summary>
    public string Id { get; }

    public int Position { get; }

    public string Name { get; }

    public string Publisher { get; }

    public string Verifier { get; }

    public long? LevelId { get; }

    public string? Video { get; }

    public int Requirement { get; }

    public DemonTier Tier { get; }

    /// <summary>Длина списка на момент чтения: по ней гаснут стрелки у краёв.</summary>
    public int ListLength { get; }

    /// <summary>Выше первого места двигать некуда.</summary>
    public bool CanMoveUp => Position > 1;

    /// <summary>Ниже последнего — тоже.</summary>
    public bool CanMoveDown => Position < ListLength;

    /// <summary>Заголовок раздела: по нему список группируется на вкладке.</summary>
    public string TierTitle => Tier.Title;

    public string TierNote => Tier.Note;

    /// <summary>«автор X · верифицировал Y» — подпись под названием, как на сайте.</summary>
    public string ByText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Publisher) && string.IsNullOrWhiteSpace(Verifier))
                return string.Empty;

            var author = string.IsNullOrWhiteSpace(Publisher) ? string.Empty : $"автор {Publisher}";
            if (string.IsNullOrWhiteSpace(Verifier))
                return author;

            var verified = $"верифицировал {Verifier}";
            return author.Length == 0 ? verified : $"{author} · {verified}";
        }
    }

    public string RequirementText => $"от {Requirement}%";

    public bool HasVideo => !string.IsNullOrWhiteSpace(Video);

    public string LevelIdText => LevelId is { } id ? $"ID {id}" : string.Empty;

    public bool HasLevelId => LevelId is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OpinionCountText))]
    [NotifyPropertyChangedFor(nameof(HasOpinions))]
    private int _opinionCount;

    public string OpinionCountText => OpinionCount > 0 ? $"{OpinionCount} ✎" : string.Empty;

    public bool HasOpinions => OpinionCount > 0;

    /// <summary>Раскрыта ли строка. Раскрытой держится только одна — как на сайте.</summary>
    [ObservableProperty] private bool _isExpanded;

    public ObservableCollection<OpinionRowViewModel> Opinions { get; } = new();

    [ObservableProperty] private bool _opinionsLoading;

    /// <summary>Показывается, пока мнений нет и они уже загружены.</summary>
    [ObservableProperty] private bool _hasNoOpinions;

    [ObservableProperty] private string _newOpinionText = string.Empty;

    /// <summary>Предлагаемое место — необязательное, поэтому строкой: пустое поле законно.</summary>
    [ObservableProperty] private string _newOpinionPosition = string.Empty;

    /// <summary>Место, на которое модератор переставляет демона.</summary>
    [ObservableProperty] private string _movePosition;
}

/// <summary>Мнение участника о размещении демона.</summary>
public sealed class OpinionRowViewModel
{
    public OpinionRowViewModel(DemonListViewModel owner, DemonRowViewModel demon, OpinionView opinion, bool canRemove)
    {
        Owner = owner;
        Demon = demon;
        Id = opinion.Id;
        AuthorName = opinion.AuthorName;
        RoleLabel = AccountViewModel.RoleLabel(opinion.AuthorRole);
        IsPrivilegedRole = opinion.AuthorRole >= UserRole.Moderator;
        SuggestedText = opinion.SuggestedPosition is { } position ? $"→ №{position}" : string.Empty;
        HasSuggestion = opinion.SuggestedPosition is not null;
        Text = opinion.Text;
        CreatedText = opinion.CreatedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
        CanRemove = canRemove;
    }

    public DemonListViewModel Owner { get; }

    /// <summary>Демон, к которому относится мнение: после удаления строка обновляет его.</summary>
    public DemonRowViewModel Demon { get; }

    public string Id { get; }

    public string AuthorName { get; }

    public string RoleLabel { get; }

    public bool IsPrivilegedRole { get; }

    public string SuggestedText { get; }

    public bool HasSuggestion { get; }

    public string Text { get; }

    public string CreatedText { get; }

    /// <summary>Своё мнение убирает автор, чужое — модератор.</summary>
    public bool CanRemove { get; }
}

/// <summary>Заявка на рекорд: строка топа, очереди модератора и списка своих заявок.</summary>
public partial class RecordRowViewModel : ObservableObject
{
    public RecordRowViewModel(DemonListViewModel owner, RecordView record)
    {
        Owner = owner;
        Id = record.Id;
        PlayerName = record.PlayerName;
        RoleLabel = AccountViewModel.RoleLabel(record.PlayerRole);
        IsPrivilegedRole = record.PlayerRole >= UserRole.Moderator;
        LevelName = record.LevelName;
        DemonPosition = record.DemonPosition;
        Progress = record.Progress;
        VideoUrl = record.VideoUrl;
        Comment = record.Comment;
        Status = record.Status;
        Placement = record.Placement;
        SubmittedText = record.SubmittedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
        ReviewNote = record.ReviewNote;
        _reviewPlacement = record.Placement?.ToString() ?? string.Empty;
    }

    public DemonListViewModel Owner { get; }

    public string Id { get; }

    public string PlayerName { get; }

    public string RoleLabel { get; }

    public bool IsPrivilegedRole { get; }

    public string LevelName { get; }

    /// <summary>Место демона в списке на момент запроса, если заявка на демона из списка.</summary>
    public int? DemonPosition { get; }

    public string DemonPositionText => DemonPosition is { } position ? $"№{position}" : "вне списка";

    public int Progress { get; }

    public string ProgressText => $"{Progress}%";

    public string VideoUrl { get; }

    public string? Comment { get; }

    public bool HasComment => !string.IsNullOrWhiteSpace(Comment);

    public RecordStatus Status { get; }

    public string StatusText => Status switch
    {
        RecordStatus.Approved => "одобрена",
        RecordStatus.Rejected => "отклонена",
        _ => "ждёт рассмотрения",
    };

    public int? Placement { get; }

    public string PlacementText => Placement is { } place ? $"#{place}" : string.Empty;

    public string SubmittedText { get; }

    public string? ReviewNote { get; }

    public bool HasReviewNote => !string.IsNullOrWhiteSpace(ReviewNote);

    /// <summary>Место в топе, которое модератор ставит при одобрении. Пусто — в конец.</summary>
    [ObservableProperty] private string _reviewPlacement;

    /// <summary>Пояснение модератора — прежде всего к отказу.</summary>
    [ObservableProperty] private string _reviewNoteInput = string.Empty;
}

/// <summary>Аккаунт в списке участников: то, что видит владелец сервера.</summary>
public partial class ManagedUserRowViewModel : ObservableObject
{
    public ManagedUserRowViewModel(DemonListViewModel owner, ManagedUserView user)
    {
        Owner = owner;
        Id = user.UserId;
        Username = user.Username;
        Email = user.Email;
        RoleLabel = AccountViewModel.RoleLabel(user.Role);
        IsPrivilegedRole = user.Role >= UserRole.Moderator;
        _banned = user.Banned;
        _listBanned = user.ListBanned;
        BanReason = user.BanReason;
        ListBanReason = user.ListBanReason;
    }

    public DemonListViewModel Owner { get; }

    public string Id { get; }

    public string Username { get; }

    public string Email { get; }

    public string RoleLabel { get; }

    public bool IsPrivilegedRole { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BanButtonText))]
    private bool _banned;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ListBanButtonText))]
    private bool _listBanned;

    public string? BanReason { get; private set; }

    public string? ListBanReason { get; private set; }

    public string BanButtonText => Banned ? "Снять бан" : "Забанить";

    public string ListBanButtonText => ListBanned ? "Вернуть в список" : "Бан в списке";

    /// <summary>Причина бана: уходит на сервер вместе с самим баном.</summary>
    [ObservableProperty] private string _reasonInput = string.Empty;

    /// <summary>Подтягивает строку под ответ сервера — бан обратим, и текст кнопок меняется.</summary>
    public void Apply(ManagedUserView user)
    {
        Banned = user.Banned;
        ListBanned = user.ListBanned;
        BanReason = user.BanReason;
        ListBanReason = user.ListBanReason;
        OnPropertyChanged(nameof(BanReason));
        OnPropertyChanged(nameof(ListBanReason));
    }
}
