using System.Windows.Controls;
using GdTracker.ViewModels;

namespace GdTracker.App.Views;

public partial class OnlineSearchPage : Page
{
    public OnlineSearchPage(OnlineSearchViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
