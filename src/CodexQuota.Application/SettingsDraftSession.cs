namespace CodexQuota.Application;

public sealed class SettingsDraftSession
{
    private readonly AppState _captured;

    public SettingsDraftSession(AppState state)
    {
        _captured = state;
        State = state with
        {
            Settings = state.Settings with
            {
                Effective = state.Settings.Persisted,
                IsEditing = true,
                HasUnsavedChanges = false
            },
            Windows = state.Windows with { IsSettingsVisible = true }
        };
    }

    public AppState State { get; private set; }

    public AppState Preview(Func<AppSettings, AppSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var effective = change(State.Settings.Effective).Normalize();
        State = State with
        {
            Settings = State.Settings with
            {
                Effective = effective,
                HasUnsavedChanges = effective != State.Settings.Persisted
            },
            Theme = State.Theme with { Requested = effective.Theme }
        };
        return State;
    }

    public AppState Commit()
    {
        var committed = State.Settings.Effective.Normalize();
        State = State with
        {
            Settings = new SettingsState(committed, committed, true, false),
            Theme = State.Theme with { Requested = committed.Theme }
        };
        return State;
    }

    public AppState Cancel()
    {
        State = _captured with
        {
            Settings = _captured.Settings with { IsEditing = false },
            Windows = _captured.Windows with { IsSettingsVisible = false }
        };
        return State;
    }
}
