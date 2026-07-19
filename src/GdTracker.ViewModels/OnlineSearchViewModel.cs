using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GdTracker.Core;
using GdTracker.Core.Abstractions;
using GdTracker.Core.Models;

namespace GdTracker.ViewModels;

/// <summary>Онлайн-поиск уровней на серверах GD и добавление найденных в трекер.</summary>
public partial class OnlineSearchViewModel : ViewModelBase
{
    private readonly IGdLevelSearch _search;
    private readonly ILevelRepository _levels;
    private readonly ISaveProgressLookupService _progressLookup;

    public OnlineSearchViewModel(
        IGdLevelSearch search, ILevelRepository levels, ISaveProgressLookupService progressLookup)
    {
        _search = search;
        _levels = levels;
        _progressLookup = progressLookup;
    }

    [ObservableProperty] private string _query = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _status;

    public ObservableCollection<OnlineLevel> Results { get; } = new();

    [RelayCommand]
    private async Task SearchAsync()
    {
        var q = Query?.Trim();
        if (string.IsNullOrEmpty(q))
        {
            Status = "Введите название или ID уровня.";
            return;
        }

        IsBusy = true;
        Status = "Поиск…";
        Results.Clear();
        try
        {
            var found = await _search.SearchAsync(q);
            foreach (var level in found)
                Results.Add(level);
            Status = found.Count == 0 ? "Ничего не найдено." : $"Найдено: {found.Count}.";
        }
        catch (Exception ex)
        {
            Status = $"Ошибка поиска: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AddToTrackerAsync(OnlineLevel? level)
    {
        if (level is null)
            return;

        var existing = await _levels.GetByGdLevelIdAsync(level.Id);
        if (existing is not null)
        {
            Status = $"«{level.Name}» уже в трекере.";
            return;
        }

        IsBusy = true;
        try
        {
            await _levels.AddAsync(new Level
            {
                GdLevelId = level.Id,
                Name = level.Name,
                Source = LevelSource.Online,
                Creator = level.Creator,
                Difficulty = level.Difficulty,
                Stars = level.Stars,
            });

            // Пользователь мог уже играть в этот уровень до его добавления через поиск —
            // подтягиваем прогресс из сейва, если он там есть, чтобы не показывать нулевые
            // попытки на уровне, который на самом деле уже пройден на сколько-то процентов.
            var progressFound = await _progressLookup.TryApplyProgressAsync(level.Id);
            Status = progressFound
                ? $"Добавлено в трекер: «{level.Name}». Прогресс подтянут из сейва."
                : $"Добавлено в трекер: «{level.Name}». В сейве этот уровень не найден.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
