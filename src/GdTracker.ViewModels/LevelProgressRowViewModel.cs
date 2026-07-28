using CommunityToolkit.Mvvm.ComponentModel;
using GdTracker.Core.Models;

namespace GdTracker.ViewModels;

/// <summary>Строка таблицы «Прогрессы»: редактируемые столбцы 1–3 поверх модели.</summary>
public partial class LevelProgressRowViewModel : ObservableObject
{
    private readonly LevelProgressRow _model;

    public LevelProgressRowViewModel(LevelProgressRow model)
    {
        _model = model;
        _practiceAttempts = model.PracticeAttempts;
        _segmentRange = model.SegmentRange;
        _toHundredRange = model.ToHundredRange;
    }

    /// <summary>Первичный ключ строки в БД.</summary>
    public int Id => _model.Id;

    [ObservableProperty] private string? _practiceAttempts;
    [ObservableProperty] private string? _segmentRange;
    [ObservableProperty] private string? _toHundredRange;

    /// <summary>
    /// Возвращает подлежащую модель с текущими значениями (обрезанными; пустое → null).
    /// Позиция строки задаётся снаружи (<see cref="LevelProgressRow.Position"/>) и здесь не меняется.
    /// </summary>
    public LevelProgressRow ToModel()
    {
        _model.PracticeAttempts = Normalize(PracticeAttempts);
        _model.SegmentRange = Normalize(SegmentRange);
        _model.ToHundredRange = Normalize(ToHundredRange);
        return _model;
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
