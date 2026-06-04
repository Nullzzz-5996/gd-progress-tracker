namespace GdTracker.ViewModels;

/// <summary>Абстракция файловых диалогов (реализуется в слое UI).</summary>
public interface IFileDialogService
{
    /// <summary>Диалог сохранения файла; возвращает путь или null при отмене.</summary>
    string? PickSaveFile(string suggestedName, string filter);

    /// <summary>Диалог открытия файла; возвращает путь или null при отмене.</summary>
    string? PickOpenFile(string filter);
}
