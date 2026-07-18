using CommunityToolkit.Mvvm.ComponentModel;
using GdTracker.Core.Models;

namespace GdTracker.ViewModels;

/// <summary>
/// Строка списка уровней: оборачивает <see cref="Models.Level"/> и добавляет
/// UI-состояние выбора (<see cref="IsSelected"/>) для группового удаления.
/// </summary>
public partial class LevelRowViewModel : ObservableObject
{
    public LevelRowViewModel(Level level) => Level = level;

    public Level Level { get; }

    [ObservableProperty]
    private bool _isSelected;
}
