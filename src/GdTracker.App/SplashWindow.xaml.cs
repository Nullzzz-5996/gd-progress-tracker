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

    /// <summary>Сколько всего этапов проходит запуск (для расчёта заполнения полосы).</summary>
    private const int TotalStages = 3;

    /// <summary>Обновляет подпись текущего этапа и продвигает полосу.</summary>
    /// <param name="stage">Название этапа по-русски.</param>
    /// <param name="stageNumber">Номер этапа, начиная с 1. По нему считается заполнение.</param>
    public void SetStage(string stage, int stageNumber)
    {
        StageText.Text = stage;
        LoadingBar.Value = Math.Clamp(stageNumber, 0, TotalStages) * 100.0 / TotalStages;
    }
}
