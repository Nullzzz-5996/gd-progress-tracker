namespace GdTracker.Core.Abstractions;

/// <summary>Настройки приложения, сохраняемые между запусками.</summary>
public interface ISettingsService
{
    /// <summary>
    /// Заданный пользователем путь к сейв-файлу GD, либо null, если не задан.
    /// Значение по умолчанию здесь не подставляется: автоопределение — забота
    /// <see cref="ISaveFileReader.DefaultSaveFilePath"/>, чтобы слой данных не знал про GameSync.
    /// </summary>
    string? SaveFilePath { get; }

    /// <summary>Задаёт (или очищает при null) путь к сейв-файлу и сразу сохраняет настройки.</summary>
    void SetSaveFilePath(string? path);

    /// <summary>Текущая тема оформления. По умолчанию (и при отсутствии/повреждении настроек) — тёмная.</summary>
    AppTheme Theme { get; }

    /// <summary>Задаёт тему оформления и сразу сохраняет настройки.</summary>
    void SetTheme(AppTheme theme);
}
