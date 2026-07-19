using GdTracker.Core.Abstractions;
using GdTracker.Core.Models;

namespace GdTracker.Data;

/// <inheritdoc />
public class SaveProgressLookupService : ISaveProgressLookupService
{
    private readonly ISettingsService _settings;
    private readonly ISaveFileReader _saveReader;
    private readonly ISaveImportService _importer;

    public SaveProgressLookupService(
        ISettingsService settings, ISaveFileReader saveReader, ISaveImportService importer)
    {
        _settings = settings;
        _saveReader = saveReader;
        _importer = importer;
    }

    public async Task<bool> TryApplyProgressAsync(long gdLevelId, CancellationToken ct = default)
    {
        var path = _settings.SaveFilePath ?? _saveReader.DefaultSaveFilePath;
        if (string.IsNullOrWhiteSpace(path))
            return false;

        IReadOnlyList<SaveLevelDto> levels;
        try
        {
            // Чтение сейва — тяжёлая синхронная операция (~1,2 с, до 600 МБ памяти на
            // файле 56 МБ), поэтому уводим её в пул потоков, чтобы не морозить UI-поток
            // вызывающей стороны (по образцу LevelsViewModel.ImportFromGameAsync).
            levels = await Task.Run(() => _saveReader.ReadLevels(path), ct);
        }
        catch
        {
            // Файл отсутствует/повреждён/занят другим процессом — прогресса нет, но это
            // не повод ронять добавление уровня в трекер.
            return false;
        }

        var match = levels.FirstOrDefault(l => l.GdLevelId == gdLevelId);
        if (match is null)
            return false;

        await _importer.ImportAsync([match], ct);
        return true;
    }
}
