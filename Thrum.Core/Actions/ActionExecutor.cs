using Thrum.Core.Models;

namespace Thrum.Core.Actions;

public interface IActionRunner
{
    void SendSingleKey(string key);
    void SendKeyShortcut(string key, bool ctrl, bool alt, bool shift, bool win);
    void SendMediaPlayPause();
    void SendVolumeUp();
    void SendVolumeDown();
    void SendVolumeMute();
    void LaunchApp(string appPath);
    void OpenUrl(string url);
}

public sealed class ActionExecutor
{
    private readonly IActionRunner _runner;
    private readonly Dictionary<string, DateTime> _lastZoneTriggerTime = new();
    private DateTime _lastGlobalTriggerTime = DateTime.MinValue;
    private readonly object _lock = new();

    /// <summary>
    /// Global debounce window in milliseconds across all actions.
    /// </summary>
    public int GlobalDebounceMs { get; set; } = 150;

    public ActionExecutor(IActionRunner runner)
    {
        _runner = runner;
    }

    /// <summary>
    /// Executes the action for a given zone if cooldown and debounce criteria are satisfied.
    /// </summary>
    /// <returns>True if action was executed; false if blocked by cooldown or debounce.</returns>
    public bool TryExecute(Zone zone)
    {
        if (zone.IsIgnore || zone.Action.Type == ActionType.None)
            return false;

        var now = DateTime.UtcNow;

        lock (_lock)
        {
            // 1. Check Global Debounce
            if ((now - _lastGlobalTriggerTime).TotalMilliseconds < GlobalDebounceMs)
            {
                return false;
            }

            // 2. Check Per-Action Cooldown
            if (_lastZoneTriggerTime.TryGetValue(zone.Id, out var lastTime))
            {
                int cooldown = Math.Max(50, zone.Action.CooldownMs);
                if ((now - lastTime).TotalMilliseconds < cooldown)
                {
                    return false;
                }
            }

            _lastGlobalTriggerTime = now;
            _lastZoneTriggerTime[zone.Id] = now;
        }

        // Execute action
        ExecuteInternal(zone.Action);
        return true;
    }

    private void ExecuteInternal(ZoneAction action)
    {
        switch (action.Type)
        {
            case ActionType.SingleKey:
                if (!string.IsNullOrWhiteSpace(action.Key))
                    _runner.SendSingleKey(action.Key);
                break;

            case ActionType.KeyShortcut:
                if (!string.IsNullOrWhiteSpace(action.Key))
                    _runner.SendKeyShortcut(action.Key, action.HasCtrl, action.HasAlt, action.HasShift, action.HasWin);
                break;

            case ActionType.MediaPlayPause:
                _runner.SendMediaPlayPause();
                break;

            case ActionType.VolumeUp:
                _runner.SendVolumeUp();
                break;

            case ActionType.VolumeDown:
                _runner.SendVolumeDown();
                break;

            case ActionType.VolumeMute:
                _runner.SendVolumeMute();
                break;

            case ActionType.LaunchApp:
                if (!string.IsNullOrWhiteSpace(action.TargetPath))
                    _runner.LaunchApp(action.TargetPath);
                break;

            case ActionType.OpenUrl:
                if (!string.IsNullOrWhiteSpace(action.TargetPath))
                    _runner.OpenUrl(action.TargetPath);
                break;
        }
    }
}
