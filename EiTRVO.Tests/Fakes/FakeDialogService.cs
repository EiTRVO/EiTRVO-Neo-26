using EiTRVO.ProEngine.Services;

namespace EiTRVO.Tests.Fakes;

public class FakeDialogService : IDialogService
{
    public bool ConfirmResult { get; set; } = true;
    public string? OpenFileResult { get; set; }
    public string? SaveFileResult { get; set; }
    public string? FolderBrowserResult { get; set; }

    public string? LastConfirmMessage { get; private set; }
    public string? LastConfirmTitle { get; private set; }
    public string? LastOpenFileTitle { get; private set; }
    public string? LastOpenFileFilter { get; private set; }

    public Task<bool> ShowConfirmAsync(string message, string title)
    {
        LastConfirmMessage = message;
        LastConfirmTitle = title;
        return Task.FromResult(ConfirmResult);
    }

    public Task<string?> ShowOpenFileDialogAsync(string title, string filter)
    {
        LastOpenFileTitle = title;
        LastOpenFileFilter = filter;
        return Task.FromResult(OpenFileResult);
    }

    public Task<string?> ShowSaveFileDialogAsync(string defaultName, string filter, string title)
    {
        return Task.FromResult(SaveFileResult);
    }

    public Task<string?> ShowFolderBrowserDialogAsync(string title)
    {
        return Task.FromResult(FolderBrowserResult);
    }
}
