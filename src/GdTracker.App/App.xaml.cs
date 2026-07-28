using System.Diagnostics;
using System.Windows;
using GdTracker.App.Services;
using GdTracker.App.Views;
using GdTracker.Core.Abstractions;
using GdTracker.Data;
using GdTracker.Data.Repositories;
using GdTracker.GameSync;
using GdTracker.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wpf.Ui;
using Wpf.Ui.Abstractions;

namespace GdTracker.App;

/// <summary>
/// Точка входа приложения. Конфигурирует DI через Generic Host,
/// применяет миграции БД и показывает главное окно.
/// </summary>
public partial class App : Application
{
    private readonly IHost _host;

    public App()
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                // Данные: фабрика контекстов (короткоживущие контексты — рекомендуемый паттерн для десктопа).
                services.AddDbContextFactory<AppDbContext>(options =>
                    options.UseSqlite(AppPaths.ConnectionString));

                // Репозитории.
                services.AddSingleton<ILevelRepository, LevelRepository>();
                services.AddSingleton<IProgressRepository, ProgressRepository>();
                services.AddSingleton<ILevelProgressRowRepository, LevelProgressRowRepository>();
                services.AddSingleton<IAccountStatsRepository, AccountStatsRepository>();
                services.AddSingleton<ISettingsService, SettingsService>();

                // Импорт из сейв-файла GD.
                services.AddSingleton<ISaveFileReader, SaveFileReader>();
                services.AddSingleton<ISaveImportService, SaveImportService>();
                services.AddSingleton<ISaveProgressLookupService, SaveProgressLookupService>();

                // Обмен прогрессом (экспорт/импорт файлами) + файловые диалоги и диалоги подтверждения.
                services.AddSingleton<IProgressSharingService, ProgressSharingService>();
                services.AddSingleton<IFileDialogService, FileDialogService>();
                services.AddSingleton<IConfirmationService, ConfirmationService>();
                // Глобальный монитор ввода для вкладки CPS (транзитный: живёт вместе со страницей/VM).
                services.AddTransient<GdTracker.ViewModels.IGlobalInputMonitor, GdTracker.App.Services.GlobalInputMonitor>();

                // Темы оформления.
                // Полные имена типов: WPF-UI сам определяет IThemeService/ThemeService
                // (Wpf.Ui), что конфликтует с нашей абстракцией того же имени.
                services.AddSingleton<GdTracker.ViewModels.IThemeService, GdTracker.App.Services.ThemeService>();

                // Палитра цветов графиков статистики: общая на всё приложение (singleton),
                // в отличие от транзиентных StatsViewModel — ThemeService оповещает её
                // о смене темы, а она уведомляет открытую страницу статистики.
                services.AddSingleton<GdTracker.App.Services.ChartPaletteService>();
                services.AddSingleton<GdTracker.ViewModels.IChartPalette>(
                    sp => sp.GetRequiredService<GdTracker.App.Services.ChartPaletteService>());

                // Онлайн-поиск уровней на серверах GD.
                services.AddSingleton<IGdLevelSearch, GdLevelSearchClient>();

                // Навигация WPF UI: провайдер страниц из DI + сервис навигации.
                services.AddSingleton<INavigationViewPageProvider, PageProvider>();
                services.AddSingleton<INavigationService, NavigationService>();
                // Контекст перехода на вкладку «Прогрессы»: какой уровень открыть.
                services.AddSingleton<IProgressNavigationContext, ProgressNavigationContext>();

                // Окна.
                services.AddSingleton<MainWindow>();

                // Страницы и их view-модели.
                services.AddTransient<DashboardPage>();
                services.AddTransient<LevelsViewModel>();
                services.AddTransient<ProgressesPage>();
                services.AddTransient<ProgressesViewModel>();
                services.AddTransient<StatsPage>();
                services.AddTransient<StatsViewModel>();
                services.AddTransient<OnlineSearchPage>();
                services.AddTransient<OnlineSearchViewModel>();
                services.AddTransient<SettingsPage>();
                services.AddTransient<SettingsViewModel>();
                services.AddTransient<CpsPage>();
                services.AddTransient<CpsViewModel>();
            })
            .Build();
    }

    /// <summary>Минимальное время показа экрана загрузки — реальные этапы запуска
    /// занимают доли секунды, и без этой паузы экран лишь мигнёт.</summary>
    private static readonly TimeSpan MinSplashDuration = TimeSpan.FromSeconds(1.5);

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Экран загрузки открывается раньше главного окна и закрывается позже него.
        // По умолчанию ShutdownMode = OnLastWindowClose: если бы splash в момент
        // своего закрытия оставался единственным открытым окном (главное окно ещё
        // не показано), приложение немедленно начало бы завершаться. Отключаем
        // автозавершение на время инициализации и включаем его обратно в конце
        // метода, уже привязав к настоящему главному окну.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var splash = new SplashWindow();
        splash.Show();

        var stopwatch = Stopwatch.StartNew();

        // Этап 1: подготовка каталогов приложения и применение миграций БД.
        splash.SetStage("Подготовка данных приложения...", stageNumber: 1);
        AppPaths.EnsureDirectories();

        // Применяем миграции: создаём/обновляем БД при запуске.
        using (var db = _host.Services
                   .GetRequiredService<IDbContextFactory<AppDbContext>>()
                   .CreateDbContext())
        {
            db.Database.Migrate();
        }

        await _host.StartAsync();

        // Этап 2: чтение настроек и применение темы оформления.
        // Тема применяется до показа главного окна, чтобы окно сразу
        // отрисовалось в выбранной теме, без промежуточного мигания дефолтной.
        // Экран загрузки уже открыт и использует DynamicResource, поэтому он
        // корректно перекрасится в момент применения темы.
        splash.SetStage("Применение темы оформления...", stageNumber: 2);
        var theme = _host.Services.GetRequiredService<ISettingsService>().Theme;
        _host.Services.GetRequiredService<GdTracker.ViewModels.IThemeService>().ApplyTheme(theme);

        // Этап 3: построение контейнера зависимостей и главного окна.
        splash.SetStage("Загрузка главного окна...", stageNumber: 3);
        var mainWindow = _host.Services.GetRequiredService<MainWindow>();

        // Ждём остаток минимального времени показа асинхронно: поток интерфейса не
        // блокируется, окно экрана загрузки остаётся отзывчивым, а индикатор
        // (IsIndeterminate) продолжает анимироваться во время ожидания.
        var remaining = MinSplashDuration - stopwatch.Elapsed;
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining);
        }

        // Явно назначаем главное окно приложения: первым показанным окном был
        // splash, и без этой строки Application.MainWindow остался бы указывать
        // на уже закрытый экран загрузки.
        MainWindow = mainWindow;
        mainWindow.Show();
        splash.Close();

        ShutdownMode = ShutdownMode.OnMainWindowClose;
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await _host.StopAsync();
        _host.Dispose();
        base.OnExit(e);
    }
}
