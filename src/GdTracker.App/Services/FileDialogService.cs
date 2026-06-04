using GdTracker.ViewModels;
using Microsoft.Win32;

namespace GdTracker.App.Services;

/// <inheritdoc />
public class FileDialogService : IFileDialogService
{
    public string? PickSaveFile(string suggestedName, string filter)
    {
        var dialog = new SaveFileDialog { FileName = suggestedName, Filter = filter };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickOpenFile(string filter)
    {
        var dialog = new OpenFileDialog { Filter = filter };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
