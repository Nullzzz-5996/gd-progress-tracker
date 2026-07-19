namespace GdTracker.ViewModels;

/// <summary>Абстракция диалога подтверждения опасных действий (реализуется в слое UI).</summary>
public interface IConfirmationService
{
    /// <summary>Показывает диалог с вопросом пользователю; возвращает true, если он подтвердил действие.</summary>
    bool Confirm(string title, string message);
}
