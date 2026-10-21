using System.Runtime.InteropServices;

namespace AnnounceMe;

// Opens Windows voice typing (Win+H): it listens and types into whatever field has focus.
public static class VoiceTyping
{
    const ushort VK_LWIN = 0x5B, VK_H = 0x48;
    const uint KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT
    {
        public uint type;
        public KEYBDINPUT ki;
        long _pad; // the union in INPUT is as big as MOUSEINPUT
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT
    {
        public ushort wVk, wScan;
        public uint dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint n, INPUT[] inputs, int size);

    static INPUT Key(ushort vk, bool up) => new() { type = 1, ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? KEYEVENTF_KEYUP : 0 } };

    public static void Start()
    {
        var keys = new[] { Key(VK_LWIN, false), Key(VK_H, false), Key(VK_H, true), Key(VK_LWIN, true) };
        SendInput((uint)keys.Length, keys, Marshal.SizeOf<INPUT>());
    }
}
