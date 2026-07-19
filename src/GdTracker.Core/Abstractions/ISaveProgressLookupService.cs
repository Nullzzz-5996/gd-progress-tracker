namespace GdTracker.Core.Abstractions;

/// <summary>
/// Подтягивает прогресс по одному уровню из сейв-файла игры и применяет его к уже
/// существующей строке в базе. Используется при добавлении уровня из онлайн-поиска:
/// сама строка уровня уже создана, но попытки и лучший процент в ней ещё нулевые,
/// хотя игрок мог уже играть в этот уровень.
/// </summary>
public interface ISaveProgressLookupService
{
    /// <summary>
    /// Ищет уровень с идентификатором <paramref name="gdLevelId"/> в сейв-файле и, если
    /// находит, применяет его прогресс через <see cref="ISaveImportService"/>. Путь к сейву
    /// берётся из <c>ISettingsService.SaveFilePath</c>, а при его отсутствии — из
    /// <c>ISaveFileReader.DefaultSaveFilePath</c>.
    /// Не бросает исключений при отсутствующем, повреждённом или недоступном файле —
    /// в этих случаях просто возвращает false.
    /// </summary>
    /// <returns>true, если уровень найден в сейве и прогресс применён; иначе false.</returns>
    Task<bool> TryApplyProgressAsync(long gdLevelId, CancellationToken ct = default);
}
