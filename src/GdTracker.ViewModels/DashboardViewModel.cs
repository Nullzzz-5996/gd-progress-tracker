using CommunityToolkit.Mvvm.ComponentModel;

namespace GdTracker.ViewModels;

/// <summary>View-модель главной страницы (список уровней). Наполняется в Фазе 1.</summary>
public partial class DashboardViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _title = "Уровни";
}
