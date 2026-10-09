namespace Thrum.Core.Models;

public enum ActionType
{
    None = 0,
    SingleKey,
    KeyShortcut,
    MediaPlayPause,
    VolumeUp,
    VolumeDown,
    VolumeMute,
    LaunchApp,
    OpenUrl
}

public sealed class ZoneAction
{
    public ActionType Type { get; set; } = ActionType.None;

    /// <summary>
    /// Key code or key name for SingleKey (e.g., "Space", "F5", "Enter")
    /// or main key in shortcut (e.g., "C", "Tab").
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Modifier keys bitmask/flags: Ctrl=1, Alt=2, Shift=4, Win=8.
    /// </summary>
    public int Modifiers { get; set; } = 0;

    /// <summary>
    /// Target executable path or web URL.
    /// </summary>
    public string TargetPath { get; set; } = string.Empty;

    /// <summary>
    /// Per-action cooldown in milliseconds (default: 500 ms).
    /// </summary>
    public int CooldownMs { get; set; } = 500;

    public bool HasCtrl => (Modifiers & 1) != 0;
    public bool HasAlt => (Modifiers & 2) != 0;
    public bool HasShift => (Modifiers & 4) != 0;
    public bool HasWin => (Modifiers & 8) != 0;

    public override string ToString()
    {
        return Type switch
        {
            ActionType.SingleKey => $"Key: {Key}",
            ActionType.KeyShortcut => $"Shortcut: {(HasCtrl ? "Ctrl+" : "")}{(HasAlt ? "Alt+" : "")}{(HasShift ? "Shift+" : "")}{(HasWin ? "Win+" : "")}{Key}",
            ActionType.MediaPlayPause => "Media: Play / Pause",
            ActionType.VolumeUp => "Volume: Up",
            ActionType.VolumeDown => "Volume: Down",
            ActionType.VolumeMute => "Volume: Mute",
            ActionType.LaunchApp => $"Launch: {Path.GetFileName(TargetPath)}",
            ActionType.OpenUrl => $"Open: {TargetPath}",
            _ => "No Action"
        };
    }
}
