using System.Windows;
using GdTracker.ViewModels;

namespace GdTracker.App.Services;

/// <inheritdoc />
public class ConfirmationService : IConfirmationService
{
    public bool Confirm(string title, string message)
    {
        // Кнопка по умолчанию — «нет», чтобы случайный Enter не подтверждал опасное действие.
        var result = MessageBox.Show(
            message,
            title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        return result == MessageBoxResult.Yes;
    }
}
