using System.Diagnostics;
using System.Runtime.InteropServices;
using Thrum.Core.Actions;

namespace Thrum.App.Services;

public sealed class Win32ActionRunner : IActionRunner
{
    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

    // Virtual-Key Codes
    private const ushort VK_LCONTROL = 0xA2;
    private const ushort VK_LMENU = 0xA4;    // Alt
    private const ushort VK_LSHIFT = 0xA0;
    private const ushort VK_LWIN = 0x5B;

    private const ushort VK_VOLUME_MUTE = 0xAD;
    private const ushort VK_VOLUME_DOWN = 0xAE;
    private const ushort VK_VOLUME_UP = 0xAF;
    private const ushort VK_MEDIA_PLAY_PAUSE = 0xB3;

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUT
    {
        [FieldOffset(0)]
        public int type;
        [FieldOffset(8)]
        public KEYBDINPUT ki;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern short VkKeyScan(char ch);

    private static void SendKeyStroke(ushort vk, bool isExtended = false)
    {
        uint flagsDown = isExtended ? KEYEVENTF_EXTENDEDKEY : 0;
        uint flagsUp = KEYEVENTF_KEYUP | (isExtended ? KEYEVENTF_EXTENDEDKEY : 0);

        var inputs = new INPUT[2];
        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].ki.wVk = vk;
        inputs[0].ki.dwFlags = flagsDown;

        inputs[1].type = INPUT_KEYBOARD;
        inputs[1].ki.wVk = vk;
        inputs[1].ki.dwFlags = flagsUp;

        SendInput(2, inputs, Marshal.SizeOf<INPUT>());
    }

    public void SendSingleKey(string keyName)
    {
        ushort vk = ParseKey(keyName);
        if (vk != 0)
        {
            SendKeyStroke(vk);
        }
    }

    public void SendKeyShortcut(string keyName, bool ctrl, bool alt, bool shift, bool win)
    {
        ushort keyVk = ParseKey(keyName);
        var inputs = new List<INPUT>();

        // 1. Press Modifiers Down
        if (ctrl) inputs.Add(MakeKeyInput(VK_LCONTROL, 0));
        if (alt) inputs.Add(MakeKeyInput(VK_LMENU, 0));
        if (shift) inputs.Add(MakeKeyInput(VK_LSHIFT, 0));
        if (win) inputs.Add(MakeKeyInput(VK_LWIN, KEYEVENTF_EXTENDEDKEY));

        // 2. Press and Release Target Key
        if (keyVk != 0)
        {
            inputs.Add(MakeKeyInput(keyVk, 0));
            inputs.Add(MakeKeyInput(keyVk, KEYEVENTF_KEYUP));
        }

        // 3. Release Modifiers Up (in reverse order)
        if (win) inputs.Add(MakeKeyInput(VK_LWIN, KEYEVENTF_KEYUP | KEYEVENTF_EXTENDEDKEY));
        if (shift) inputs.Add(MakeKeyInput(VK_LSHIFT, KEYEVENTF_KEYUP));
        if (alt) inputs.Add(MakeKeyInput(VK_LMENU, KEYEVENTF_KEYUP));
        if (ctrl) inputs.Add(MakeKeyInput(VK_LCONTROL, KEYEVENTF_KEYUP));

        if (inputs.Count > 0)
        {
            SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
        }
    }

    public void SendMediaPlayPause() => SendKeyStroke(VK_MEDIA_PLAY_PAUSE, true);
    public void SendVolumeUp() => SendKeyStroke(VK_VOLUME_UP, true);
    public void SendVolumeDown() => SendKeyStroke(VK_VOLUME_DOWN, true);
    public void SendVolumeMute() => SendKeyStroke(VK_VOLUME_MUTE, true);

    public void LaunchApp(string appPath)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(appPath) && System.IO.File.Exists(appPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = appPath,
                    UseShellExecute = true
                });
            }
        }
        catch
        {
            // Ignore execution failure
        }
    }

    public void OpenUrl(string url)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(url))
            {
                if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    url = "https://" + url;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
        }
        catch
        {
            // Ignore browser start failure
        }
    }

    private static INPUT MakeKeyInput(ushort vk, uint flags)
    {
        var input = new INPUT
        {
            type = INPUT_KEYBOARD
        };
        input.ki.wVk = vk;
        input.ki.dwFlags = flags;
        return input;
    }

    private static ushort ParseKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return 0;

        string trimmed = key.Trim().ToUpperInvariant();

        return trimmed switch
        {
            "SPACE" => 0x20,
            "ENTER" or "RETURN" => 0x0D,
            "ESC" or "ESCAPE" => 0x1B,
            "TAB" => 0x09,
            "BACKSPACE" => 0x08,
            "LEFT" => 0x25,
            "UP" => 0x26,
            "RIGHT" => 0x27,
            "DOWN" => 0x28,
            "F1" => 0x70,
            "F2" => 0x71,
            "F3" => 0x72,
            "F4" => 0x73,
            "F5" => 0x74,
            "F6" => 0x75,
            "F7" => 0x76,
            "F8" => 0x77,
            "F9" => 0x78,
            "F10" => 0x79,
            "F11" => 0x7A,
            "F12" => 0x7B,
            _ => trimmed.Length == 1 ? (ushort)(VkKeyScan(trimmed[0]) & 0xFF) : (ushort)0
        };
    }
}
