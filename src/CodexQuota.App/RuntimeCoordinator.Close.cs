using CodexQuota.Application;
using CodexQuota.UI.Avalonia;

namespace CodexQuota.App;

internal sealed partial class RuntimeCoordinator
{
    private bool _closePromptOpen;

    private async Task RestoreOrbFromTrayAsync()
    {
        if (_shuttingDown || _dashboardHiding || _dashboardOpening || _displayRebuilding || _closePromptOpen || _orb is null) return;
        if (_dashboard?.IsPresented == true) { await CollapseDashboardAsync(); return; }
        _orb.ExpandFromEdge();
        _orb.Show();
        _orb.RestoreRememberedPosition(_settings.OrbX, _settings.OrbY, _settings.OrbDisplayId);
        ApplyNativeOrbSettings();
        _settings = _settings with { LastView = StartupViewMode.Orb };
        try { await _settingsStore.WriteAsync(_settings, _lifetime.Token); }
        catch (OperationCanceledException) when (_shuttingDown) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Restored view remains usable. */ }
    }

    private async Task CloseDashboardAsync()
    {
        if (_shuttingDown || _closePromptOpen || _dashboardOpening || _dashboardHiding || _displayRebuilding || _dashboard is null) return;
        _closePromptOpen = true;
        try
        {
            var action = _settings.CloseAction;
            if (action == PanelCloseAction.Ask)
            {
                var prompt = new PanelCloseWindow(_settings, IsSystemDark());
                PrepareNativeWindowTheme(prompt);
                action = await prompt.ShowDialog<PanelCloseAction>(_dashboard);
                if (_shuttingDown || action == PanelCloseAction.Ask) return;
                if (prompt.RememberChoice)
                {
                    _settings = _settings with { CloseAction = action };
                    await _settingsStore.WriteAsync(_settings, _lifetime.Token);
                }
            }
            if (_shuttingDown) return;
            if (action == PanelCloseAction.Exit) { Exit(); return; }
            _dashboardHiding = true;
            try
            {
                await _dashboard.AnimateOutAsync();
                _orb?.Hide();
                _settings = _settings with { LastView = StartupViewMode.TrayOnly };
                await _settingsStore.WriteAsync(_settings, _lifetime.Token);
            }
            finally { _dashboardHiding = false; }
        }
        catch (OperationCanceledException) when (_shuttingDown) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!_shuttingDown) await ShowMessageAsync(T("保存失败", "Save failed"),
                T("无法保存关闭偏好，请检查设置目录权限后重试。", "Could not save the close preference. Check settings folder permissions and retry."));
        }
        finally { _closePromptOpen = false; }
    }
}
