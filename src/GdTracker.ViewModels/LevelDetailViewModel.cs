using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GdTracker.Core;
using GdTracker.Core.Abstractions;
using GdTracker.Core.Models;
using GdTracker.Core.Services;

namespace GdTracker.ViewModels;

/// <summary>View-модель детали уровня: записи прогресса + форма добавления.</summary>
public partial class LevelDetailViewModel : ViewModelBase
{
    private readonly ILevelRepository _levels;
    private readonly IProgressRepository _progress;
    private int _levelId;

    public LevelDetailViewModel(ILevelRepository levels, IProgressRepository progress)
    {
        _levels = levels;
        _progress = progress;
    }

    /// <summary>Срабатывает после изменения прогресса (для обновления списка уровней).</summary>
    public event Action? Changed;

    [ObservableProperty]
    private Level? _level;

    public ObservableCollection<ProgressRecord> Records { get; } = new();

    public RunType[] RunTypes { get; } = Enum.GetValues<RunType>();
    public ProgressMode[] Modes { get; } = Enum.GetValues<ProgressMode>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSegment))]
    private RunType _newRunType = RunType.FromZero;

    [ObservableProperty] private ProgressMode _newMode = ProgressMode.Normal;
    [ObservableProperty] private int _newStartPercent;
    [ObservableProperty] private int _newReachedPercent;
    [ObservableProperty] private string? _newAttempts;
    [ObservableProperty] private string? _newNote;
    [ObservableProperty] private string? _formError;

    /// <summary>true для сегмента (стартовый процент редактируем).</summary>
    public bool IsSegment => NewRunType == RunType.Segment;

    partial void OnNewRunTypeChanged(RunType value)
    {
        if (value == RunType.FromZero)
            NewStartPercent = 0;
    }

    public async Task LoadAsync(int levelId)
    {
        _levelId = levelId;
        var level = await _levels.GetByIdAsync(levelId);
        Level = level;

        Records.Clear();
        if (level is not null)
        {
            foreach (var r in level.ProgressRecords.OrderByDescending(r => r.Date))
                Records.Add(r);
        }
    }

    [RelayCommand]
    private async Task AddProgressAsync()
    {
        FormError = null;

        var validation = ProgressValidator.Validate(NewRunType, NewStartPercent, NewReachedPercent);
        if (!validation.IsValid)
        {
            FormError = validation.Error;
            return;
        }

        int? attempts = null;
        if (!string.IsNullOrWhiteSpace(NewAttempts))
        {
            if (!int.TryParse(NewAttempts, out var parsed) || parsed < 0)
            {
                FormError = "Количество попыток должно быть неотрицательным числом.";
                return;
            }
            attempts = parsed;
        }

        await _progress.AddAsync(new ProgressRecord
        {
            LevelId = _levelId,
            Type = NewRunType,
            StartPercent = NewStartPercent,
            ReachedPercent = NewReachedPercent,
            Mode = NewMode,
            Attempts = attempts,
            Note = string.IsNullOrWhiteSpace(NewNote) ? null : NewNote.Trim(),
            Date = DateTime.UtcNow,
            Source = ProgressSource.Manual,
        });

        // Сброс формы и перезагрузка.
        NewReachedPercent = 0;
        NewAttempts = null;
        NewNote = null;

        await LoadAsync(_levelId);
        Changed?.Invoke();
    }

    [RelayCommand]
    private async Task DeleteProgressAsync(ProgressRecord? record)
    {
        if (record is null)
            return;

        await _progress.DeleteAsync(record.Id);
        await LoadAsync(_levelId);
        Changed?.Invoke();
    }
}
