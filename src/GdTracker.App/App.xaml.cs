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

                // Окна.
                services.AddSingleton<MainWindow>();

                // Страницы и их view-модели.
                services.AddTransient<DashboardPage>();
                services.AddTransient<LevelsViewModel>();
                services.AddTransient<StatsPage>();
                services.AddTransient<StatsViewModel>();
                services.AddTransient<OnlineSearchPage>();
                services.AddTransient<OnlineSearchViewModel>();
                services.AddTransient<SettingsPage>();
                services.AddTransient<SettingsViewModel>();
            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppPaths.EnsureDirectories();

        // Применяем миграции: создаём/обновляем БД при запуске.
        using (var db = _host.Services
                   .GetRequiredService<IDbContextFactory<AppDbContext>>()
                   .CreateDbContext())
        {
            db.Database.Migrate();
        }

        await _host.StartAsync();

        // Тема применяется до показа главного окна, чтобы окно сразу
        // отрисовалось в выбранной теме, без промежуточного мигания дефолтной.
        var theme = _host.Services.GetRequiredService<ISettingsService>().Theme;
        _host.Services.GetRequiredService<GdTracker.ViewModels.IThemeService>().ApplyTheme(theme);

        _host.Services.GetRequiredService<MainWindow>().Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await _host.StopAsync();
        _host.Dispose();
        base.OnExit(e);
    }
}
