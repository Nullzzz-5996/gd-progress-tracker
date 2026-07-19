namespace GdTracker.Core;

/// <summary>Происхождение уровня.</summary>
public enum LevelSource
{
    /// <summary>Официальный уровень игры (Stereo Madness и т.д.).</summary>
    Official,

    /// <summary>Онлайн/пользовательский уровень, идентифицируемый по GD Level ID.</summary>
    Online,

    /// <summary>Уровень, добавленный вручную пользователем (без привязки к GD).</summary>
    Custom,
}

/// <summary>Режим прохождения.</summary>
public enum ProgressMode
{
    /// <summary>Обычный режим (normal) — засчитывается как реальное прохождение.</summary>
    Normal,

    /// <summary>Режим практики (practice).</summary>
    Practice,
}

/// <summary>Тип записи прогресса.</summary>
public enum RunType
{
    /// <summary>Прогресс с нуля процентов: 0% → Y%.</summary>
    FromZero,

    /// <summary>Сегмент: X% → Y%, где X ≠ 0.</summary>
    Segment,
}

/// <summary>Источник записи прогресса.</summary>
public enum ProgressSource
{
    /// <summary>Введено вручную пользователем.</summary>
    Manual,

    /// <summary>Импортировано из сейв-файла GD (CCGameManager.dat).</summary>
    SaveImport,

    /// <summary>Захвачено в реальном времени (Geode-мост / чтение памяти).</summary>
    Live,
}

/// <summary>Тема оформления приложения.</summary>
public enum AppTheme
{
    /// <summary>Тёмная тема (по умолчанию).</summary>
    Dark,

    /// <summary>Светлая тема.</summary>
    Light,

    /// <summary>Неоновая тема.</summary>
    Neon,
}
