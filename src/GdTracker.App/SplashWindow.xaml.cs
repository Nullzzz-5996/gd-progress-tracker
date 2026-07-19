using System.Windows;

namespace GdTracker.App;

/// <summary>
/// Экран загрузки. Показывается сразу при запуске приложения — раньше, чем
/// применена тема оформления, поэтому все кисти подключены через
/// <c>DynamicResource</c> и корректно перекрашиваются, как только
/// <see cref="Services.ThemeService"/> подмешивает словарь темы.
/// Подпись этапа обновляется из <see cref="App.OnStartup"/> по мере
/// прохождения реальных этапов инициализации — полоса не декоративный
/// таймер, а отражение фактического хода запуска.
/// </summary>
public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
    }

    /// <summary>Обновляет подпись текущего этапа загрузки.</summary>
    /// <param name="stage">Название этапа по-русски.</param>
    public void SetStage(string stage)
    {
        StageText.Text = stage;
    }
}
