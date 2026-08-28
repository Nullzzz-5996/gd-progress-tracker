using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GdTracker.Core.Abstractions;
using GdTracker.Core.Models;

namespace GdTracker.ViewModels;

/// <summary>Вкладка «Прогрессы»: выбор уровня + таблица из 4 столбцов (до 100 строк на уровень).</summary>
public partial class ProgressesViewModel : ViewModelBase
{
    private const int MaxRows = 100;

    private readonly ILevelRepository _levels;
    private readonly ILevelProgressRowRepository _rows;
    private readonly IProgressNavigationContext _navContext;

    public ProgressesViewModel(
        ILevelRepository levels,
        ILevelProgressRowRepository rows,
        IProgressNavigationContext navContext)
    {
        _levels = levels;
        _rows = rows;
        _navContext = navContext;
    }

    /// <summary>Уровни для переключателя.</summary>
    public ObservableCollection<Level> Levels { get; } = new();

    /// <summary>Строки таблицы выбранного уровня.</summary>
    public ObservableCollection<LevelProgressRowViewModel> Rows { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FromZeroDisplay))]
    private Level? _selectedLevel;

    [ObservableProperty] private string? _status;

    /// <summary>
    /// Пока true, обработчик выбора уровня в UI не перезагружает строки: стартовый уровень
    /// загружает сам <see cref="LoadAsync"/>, без гонки с событием SelectionChanged у ComboBox.
    /// </summary>
    public bool IsLoadingLevels { get; private set; }

    /// <summary>Столбец 4 «с нуля»: 0–лучший normal % выбранного уровня.</summary>
    public string FromZeroDisplay => SelectedLevel is null ? string.Empty : $"0-{SelectedLevel.BestNormalPercent}";

    private bool CanAddRow => SelectedLevel is not null && Rows.Count < MaxRows;

    /// <summary>Грузит уровни, выбирает целевой (из контекста навигации) или первый, грузит его строки.</summary>
    public async Task LoadAsync()
    {
        Status = null;
        IReadOnlyList<Level> all;
        try
        {
            all = await _levels.GetTrackedAsync();
        }
        catch (Exception ex)
        {
            Status = $"Не удалось загрузить уровни: {ex.Message}";
            return;
        }

        IsLoadingLevels = true;
        try
        {
            Levels.Clear();
            foreach (var l in all)
                Levels.Add(l);

            var targetId = _navContext.TargetLevelId;
            _navContext.TargetLevelId = null; // одноразово: возврат на вкладку позже не должен перепрыгивать
            SelectedLevel = (targetId is not null ? Levels.FirstOrDefault(l => l.Id == targetId) : null)
                            ?? Levels.FirstOrDefault();
        }
        finally
        {
            IsLoadingLevels = false;
        }

        await LoadRowsAsync();
    }

    /// <summary>
    /// Перезагружает строки для текущего <see cref="SelectedLevel"/>. Исключения не
    /// выпускаются наружу: метод вызывается из LoadAsync (сама вызывается из обработчика
    /// Loaded страницы — эквивалент async void) и из обработчика SelectionChanged, что
    /// буквально является async void — необработанное исключение уронило бы приложение.
    /// </summary>
    public async Task LoadRowsAsync()
    {
        Status = null;
        try
        {
            Rows.Clear();
            if (SelectedLevel is not null)
            {
                var loaded = await _rows.GetByLevelAsync(SelectedLevel.Id);
                foreach (var r in loaded)
                    Rows.Add(new LevelProgressRowViewModel(r));
            }
        }
        catch (Exception ex)
        {
            Status = $"Не удалось загрузить строки: {ex.Message}";
            return;
        }

        AddRowCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanAddRow))]
    private async Task AddRowAsync()
    {
        if (!CanAddRow)
            return;

        var model = new LevelProgressRow { LevelId = SelectedLevel!.Id, Position = Rows.Count + 1 };
        var saved = await _rows.AddAsync(model);
        Rows.Add(new LevelProgressRowViewModel(saved));
        AddRowCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Удаляет строку и перенумеровывает оставшиеся. Вызывается из
    /// DeleteRowCommand, привязанной к кнопке в шаблоне ячейки — необработанное исключение
    /// здесь ведёт себя как в async void и уронило бы приложение.</summary>
    [RelayCommand]
    private async Task DeleteRowAsync(LevelProgressRowViewModel? row)
    {
        if (row is null)
            return;

        Status = null;
        try
        {
            await _rows.DeleteAsync(row.Id);
            Rows.Remove(row);

            // Перенумеровываем оставшиеся строки, чтобы позиции шли подряд 1..N.
            for (int i = 0; i < Rows.Count; i++)
            {
                var model = Rows[i].ToModel();
                model.Position = i + 1;
                await _rows.UpdateAsync(model);
            }
        }
        catch (Exception ex)
        {
            Status = $"Не удалось удалить строку: {ex.Message}";
            return;
        }

        AddRowCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Сохраняет отредактированную строку. Вызывается из RowEditEnding страницы, что
    /// является async void — необработанное исключение уронило бы приложение целиком.
    /// </summary>
    public async Task SaveRowAsync(LevelProgressRowViewModel row)
    {
        Status = null;
        try
        {
            await _rows.UpdateAsync(row.ToModel());
        }
        catch (Exception ex)
        {
            Status = $"Не удалось сохранить строку: {ex.Message}";
        }
    }
}
