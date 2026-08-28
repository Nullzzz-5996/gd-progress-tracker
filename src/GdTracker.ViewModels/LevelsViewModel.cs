using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GdTracker.Core;
using GdTracker.Core.Abstractions;
using GdTracker.Core.Models;

namespace GdTracker.ViewModels;

/// <summary>
/// Страница «Уровни»: список с локальным фильтром и групповым выделением/удалением,
/// деталь выбранного уровня, добавление, импорт из игры и обмен файлами.
/// </summary>
public partial class LevelsViewModel : ViewModelBase
{
    private readonly ILevelRepository _levels;
    private readonly IProgressRepository _progress;
    private readonly ISaveFileReader _saveReader;
    private readonly ISaveImportService _importer;
    private readonly IProgressSharingService _sharing;
    private readonly IFileDialogService _fileDialog;
    private readonly IConfirmationService _confirmation;
    private readonly ISettingsService _settings;
    private readonly ISaveProgressLookupService _progressLookup;

    private readonly List<LevelRowViewModel> _allRows = new();

    public LevelsViewModel(
        ILevelRepository levels,
        IProgressRepository progress,
        ISaveFileReader saveReader,
        ISaveImportService importer,
        IProgressSharingService sharing,
        IFileDialogService fileDialog,
        IConfirmationService confirmation,
        ISettingsService settings,
        ISaveProgressLookupService progressLookup)
    {
        _levels = levels;
        _progress = progress;
        _saveReader = saveReader;
        _importer = importer;
        _sharing = sharing;
        _fileDialog = fileDialog;
        _confirmation = confirmation;
        _settings = settings;
        _progressLookup = progressLookup;
        _saveFilePath = settings.SaveFilePath ?? saveReader.DefaultSaveFilePath ?? string.Empty;
    }

    /// <summary>Отфильтрованный список строк, отображаемый в сетке.</summary>
    public ObservableCollection<LevelRowViewModel> Levels { get; } = new();

    public LevelSource[] Sources { get; } = Enum.GetValues<LevelSource>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private LevelRowViewModel? _selectedRow;

    [ObservableProperty] private LevelDetailViewModel? _detail;
    [ObservableProperty] private int _selectedCount;
    [ObservableProperty] private string _filter = string.Empty;

    [ObservableProperty] private string _newLevelName = string.Empty;
    [ObservableProperty] private LevelSource _newLevelSource = LevelSource.Custom;

    /// <summary>Необязательный ID уровня в игре, введённый в форме ручного добавления.</summary>
    [ObservableProperty] private string _newLevelGdId = string.Empty;

    [ObservableProperty] private string? _error;

    [ObservableProperty] private string _saveFilePath = string.Empty;
    [ObservableProperty] private string? _importStatus;
    [ObservableProperty] private bool _isBusy;

    public bool HasSelection => SelectedRow is not null;

    /// <summary>Сохраняет изменённый пользователем путь, чтобы он пережил перезапуск.</summary>
    partial void OnSaveFilePathChanged(string value)
        => _settings.SetSaveFilePath(string.IsNullOrWhiteSpace(value) ? null : value);

    /// <summary>
    /// Загружает список уровней. Исключения не выпускаются наружу: метод вызывается из
    /// обработчика Loaded страницы, что эквивалентно async void — необработанное исключение
    /// уронило бы приложение целиком. Также вызывается из команд добавления, удаления и
    /// импорта после их успешного выполнения: ошибка перезагрузки списка сообщается через
    /// Error и остаётся видимой пользователю, а не проглатывается молча.
    /// </summary>
    public async Task LoadAsync()
    {
        var selectedId = SelectedRow?.Level.Id;

        IReadOnlyList<Level> all;
        try
        {
            all = await _levels.GetTrackedAsync();
        }
        catch (Exception ex)
        {
            Error = $"Не удалось загрузить список уровней: {ex.Message}";
            return;
        }

        Error = null;
        _allRows.Clear();
        foreach (var level in all)
        {
            var row = new LevelRowViewModel(level);
            row.PropertyChanged += OnRowPropertyChanged;
            _allRows.Add(row);
        }

        ApplyFilter();
        UpdateSelectedCount();

        if (selectedId is not null)
            SelectedRow = Levels.FirstOrDefault(r => r.Level.Id == selectedId);
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LevelRowViewModel.IsSelected))
            UpdateSelectedCount();
    }

    private void UpdateSelectedCount() => SelectedCount = _allRows.Count(r => r.IsSelected);

    partial void OnFilterChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var f = Filter?.Trim();
        IEnumerable<LevelRowViewModel> rows = _allRows;

        if (!string.IsNullOrEmpty(f))
        {
            rows = _allRows.Where(r =>
                r.Level.Name.Contains(f, StringComparison.OrdinalIgnoreCase)
                || (r.Level.Creator?.Contains(f, StringComparison.OrdinalIgnoreCase) ?? false)
                || (r.Level.Difficulty?.Contains(f, StringComparison.OrdinalIgnoreCase) ?? false)
                || (r.Level.GdLevelId?.ToString().Contains(f) ?? false));
        }

        Levels.Clear();
        foreach (var r in rows)
            Levels.Add(r);
    }

    partial void OnSelectedRowChanged(LevelRowViewModel? value) => _ = LoadDetailAsync(value?.Level);

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

        // ID уровня в игре необязателен: без него уровень остаётся «ручной заметкой» и не
        // привязывается к сейву. Если он введён, но не является числом — это ошибка ввода,
        // а не повод создавать уровень без привязки.
        long? gdLevelId = null;
        var gdIdText = NewLevelGdId?.Trim();
        if (!string.IsNullOrEmpty(gdIdText))
        {
            if (!long.TryParse(gdIdText, out var parsedId))
            {
                Error = "ID уровня должен быть числом.";
                return;
            }

            gdLevelId = parsedId;
        }

        IsBusy = true;
        try
        {
            // Уровень мог уже попасть в базу импортом из игры: такие строки скрыты из списка,
            // но хранят всю информацию (звёзды, создатель, сложность, лучший процент, попытки).
            // Ручное добавление показывает найденную строку, а не заводит рядом вторую.
            var existing = gdLevelId is not null
                ? await _levels.GetByGdLevelIdAsync(gdLevelId.Value)
                : await _levels.FindUntrackedByNameAsync(name);

            if (existing is not null && existing.IsTracked)
            {
                Error = $"Уровень с ID {gdLevelId} уже есть в базе: «{existing.Name}».";
                return;
            }

            int levelId;
            if (existing is not null)
            {
                // Название из игры точнее введённого вручную, поэтому оставляем его; введённое
                // берём, только если из сейва имя не пришло (там подставляется ID уровня).
                if (string.IsNullOrWhiteSpace(existing.Name)
                    || existing.Name == existing.GdLevelId?.ToString())
                {
                    existing.Name = name;
                }

                existing.IsTracked = true;
                await _levels.UpdateAsync(existing);
                levelId = existing.Id;
            }
            else
            {
                var level = await _levels.AddAsync(
                    new Level { Name = name, Source = NewLevelSource, GdLevelId = gdLevelId });
                levelId = level.Id;

                // Пользователь мог уже играть в этот уровень до его ручного добавления —
                // подтягиваем прогресс из сейва, если он там есть (как при добавлении из
                // онлайн-поиска), чтобы не показывать нулевые попытки на уже пройденном уровне
                // и чтобы последующий импорт из игры не завёл рядом вторую строку без ID.
                if (gdLevelId is not null)
                    await _progressLookup.TryApplyProgressAsync(gdLevelId.Value);
            }

            NewLevelName = string.Empty;
            NewLevelGdId = string.Empty;
            await LoadAsync();
            SelectedRow = Levels.FirstOrDefault(r => r.Level.Id == levelId);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var row in Levels)
            row.IsSelected = true;
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var row in _allRows)
            row.IsSelected = false;
    }

    [RelayCommand]
    private async Task DeleteCurrentLevelAsync()
    {
        Error = null;
        if (SelectedRow is null)
        {
            Error = "Сначала выберите уровень.";
            return;
        }

        var levelName = SelectedRow.Level.Name;
        var confirmed = _confirmation.Confirm(
            "Удаление уровня",
            $"Удалить уровень «{levelName}»?\n\nЭто действие необратимо: вместе с уровнем будут удалены все записи о прогрессе по нему.");
        if (!confirmed)
            return;

        await _levels.DeleteAsync(SelectedRow.Level.Id);
        SelectedRow = null;
        Detail = null;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        Error = null;
        var ids = _allRows.Where(r => r.IsSelected).Select(r => r.Level.Id).ToList();
        if (ids.Count == 0)
        {
            Error = "Не выбрано ни одного уровня.";
            return;
        }

        var confirmed = _confirmation.Confirm(
            "Удаление уровней",
            $"Удалить выбранные уровни ({ids.Count})?\n\nЭто действие необратимо: вместе с уровнями будут удалены все записи о прогрессе по ним.");
        if (!confirmed)
            return;

        await _levels.DeleteManyAsync(ids);
        SelectedRow = null;
        Detail = null;
        await LoadAsync();
        ImportStatus = $"Удалено уровней: {ids.Count}.";
    }

    [RelayCommand]
    private async Task ImportFromGameAsync()
    {
        ImportStatus = null;

        if (string.IsNullOrWhiteSpace(SaveFilePath) || !File.Exists(SaveFilePath))
        {
            ImportStatus = "Файл сейва не найден. Проверьте путь.";
            return;
        }

        IsBusy = true;
        try
        {
            var path = SaveFilePath;
            var dtos = await Task.Run(() => _saveReader.ReadLevels(path));
            var result = await _importer.ImportAsync(dtos);
            await LoadAsync();
            ImportStatus =
                $"Импортировано {result.Total} уровней (новых: {result.LevelsAdded}, обновлено: {result.LevelsUpdated}). "
                + "Данные учтены в статистике; в списке уровни не показываются — добавьте нужный вручную.";
        }
        catch (Exception ex)
        {
            ImportStatus = $"Ошибка импорта: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        var path = _fileDialog.PickSaveFile("gd-progress.json", "JSON (*.json)|*.json|Все файлы (*.*)|*.*");
        if (path is null)
            return;

        IsBusy = true;
        try
        {
            await _sharing.ExportAsync(path);
            ImportStatus = $"Экспортировано в {Path.GetFileName(path)}.";
        }
        catch (Exception ex)
        {
            ImportStatus = $"Ошибка экспорта: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ImportFromFileAsync()
    {
        var path = _fileDialog.PickOpenFile("JSON (*.json)|*.json|Все файлы (*.*)|*.*");
        if (path is null)
            return;

        IsBusy = true;
        try
        {
            var summary = await _sharing.ImportAsync(path);
            await LoadAsync();
            ImportStatus =
                $"Импорт из файла: уровней +{summary.LevelsAdded}, обновлено {summary.LevelsUpdated}, записей +{summary.RecordsAdded}.";
        }
        catch (Exception ex)
        {
            ImportStatus = $"Ошибка импорта: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
