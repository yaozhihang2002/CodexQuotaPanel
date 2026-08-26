using CodexQuota.Application;

namespace CodexQuota.UI.Avalonia;

public sealed partial class SettingsWindow
{
    internal AppSettings DraftSettings => _draft;
    internal AppSettings PersistedSettings => _persisted;
    internal int SelectedPageIndex => _selectedPage;
    internal int CachedPageCount => _pageControls.Count;
    internal int VisiblePageCount => _pageControls.Count(page => page.IsVisible);

    internal void PreviewSettings(Func<AppSettings, AppSettings> change) => Change(change);
    internal void NavigateToPage(int index) => SelectPage(index);
    internal void RequestSave() => SaveRequested?.Invoke(_draft.Normalize());
    internal void CancelEdits() => RequestCancel();
}
