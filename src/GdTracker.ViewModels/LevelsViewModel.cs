using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GdTracker.Core;
using GdTracker.Core.Abstractions;
using GdTracker.Core.Models;

namespace GdTracker.ViewModels;

/// <summary>
/// Главная страница «Уровни»: список уровней (master), деталь выбранного уровня,
/// форма добавления уровня.
/// </summary>
public partial class LevelsViewModel : ViewModelBase
{
    private readonly ILevelRepository _levels;
    private readonly IProgressRepository _progress;

    public LevelsViewModel(ILevelRepository levels, IProgressRepository progress)
    {
        _levels = levels;
        _progress = progress;
    }

    public ObservableCollection<Level> Levels { get; } = new();

    public LevelSource[] Sources { get; } = Enum.GetValues<LevelSource>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private Level? _selectedLevel;

    [ObservableProperty]
    private LevelDetailViewModel? _detail;

    [ObservableProperty] private string _newLevelName = string.Empty;
    [ObservableProperty] private LevelSource _newLevelSource = LevelSource.Custom;
    [ObservableProperty] private string? _error;

    public bool HasSelection => SelectedLevel is not null;

    public async Task LoadAsync()
    {
        var selectedId = SelectedLevel?.Id;

        var all = await _levels.GetAllAsync();
        Levels.Clear();
        foreach (var level in all)
            Levels.Add(level);

        if (selectedId is not null)
            SelectedLevel = Levels.FirstOrDefault(l => l.Id == selectedId);
    }

    partial void OnSelectedLevelChanged(Level? value) => _ = LoadDetailAsync(value);

    private async Task LoadDetailAsync(Level? level)
    {
        if (level is null)
        {
            Detail = null;
            return;
        }

        var detail = new LevelDetailViewModel(_levels, _progress);
        detail.Changed += OnDetailChanged;
        await detail.LoadAsync(level.Id);
        Detail = detail;
    }

    private void OnDetailChanged() => _ = LoadAsync();

    [RelayCommand]
    private async Task AddLevelAsync()
    {
        Error = null;
        var name = NewLevelName?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            Error = "Введите название уровня.";
            return;
        }

        var level = await _levels.AddAsync(new Level
        {
            Name = name,
            Source = NewLevelSource,
        });

        NewLevelName = string.Empty;
        await LoadAsync();
        SelectedLevel = Levels.FirstOrDefault(l => l.Id == level.Id);
    }

    [RelayCommand]
    private async Task DeleteSelectedLevelAsync()
    {
        if (SelectedLevel is null)
            return;

        await _levels.DeleteAsync(SelectedLevel.Id);
        SelectedLevel = null;
        Detail = null;
        await LoadAsync();
    }
}
