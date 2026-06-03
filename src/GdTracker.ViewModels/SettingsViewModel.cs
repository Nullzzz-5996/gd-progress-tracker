using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GdTracker.ViewModels;

/// <summary>View-модель страницы настроек. Наполняется в Фазе 2 (путь к сейву и пр.).</summary>
public partial class SettingsViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _appVersion =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
}
