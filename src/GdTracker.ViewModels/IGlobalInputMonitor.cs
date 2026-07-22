namespace GdTracker.ViewModels;

/// <summary>Глобальный монитор ввода (реализуется в слое App через низкоуровневые хуки WinAPI).</summary>
public interface IGlobalInputMonitor
{
    /// <summary>Засчитанное нажатие (ЛКМ / ↑ / W / пробел).</summary>
    event Action? Pressed;

    /// <summary>Запрошено завершение замера (нажат Escape).</summary>
    event Action? StopRequested;

    /// <summary>Ставит хуки и начинает слежение.</summary>
    void Start();

    /// <summary>Снимает хуки.</summary>
    void Stop();
}
