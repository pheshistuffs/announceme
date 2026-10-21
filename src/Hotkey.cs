using System.Runtime.InteropServices;

namespace AnnounceMe;

// A system-wide hotkey through RegisterHotKey, delivered to a hidden message window.
public class GlobalHotkey : NativeWindow, IDisposable
{
    const int WM_HOTKEY = 0x0312;
    const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_NOREPEAT = 0x4000;
    const int Id = 1;

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    bool _registered;
    public event Action Pressed;

    public GlobalHotkey() => CreateHandle(new CreateParams());

    // Returns false when the combination is taken by another program.
    public bool Set(Keys keys)
    {
        Clear();
        if (keys == Keys.None) return true;
        uint mods = MOD_NOREPEAT;
        if (keys.HasFlag(Keys.Control)) mods |= MOD_CONTROL;
        if (keys.HasFlag(Keys.Alt)) mods |= MOD_ALT;
        if (keys.HasFlag(Keys.Shift)) mods |= MOD_SHIFT;
        var key = keys & Keys.KeyCode;
        _registered = RegisterHotKey(Handle, Id, mods, (uint)key);
        return _registered;
    }

    public void Clear()
    {
        if (_registered) UnregisterHotKey(Handle, Id);
        _registered = false;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && m.WParam == Id) Pressed?.Invoke();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        Clear();
        DestroyHandle();
    }

    // "Ctrl+Alt+N" <-> Keys.
    public static Keys Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Keys.None;
        Keys mods = Keys.None, key = Keys.None;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl": mods |= Keys.Control; break;
                case "alt": mods |= Keys.Alt; break;
                case "shift": mods |= Keys.Shift; break;
                default:
                    if (raw.Length == 1 && char.IsDigit(raw[0])) key = Keys.D0 + (raw[0] - '0');
                    else if (Enum.TryParse<Keys>(raw, true, out var k)) key = k & Keys.KeyCode;
                    break;
            }
        }
        return key == Keys.None ? Keys.None : mods | key;
    }

    public static string Format(Keys keys)
    {
        var key = keys & Keys.KeyCode;
        if (key == Keys.None) return "";
        var parts = new List<string>();
        if (keys.HasFlag(Keys.Control)) parts.Add("Ctrl");
        if (keys.HasFlag(Keys.Alt)) parts.Add("Alt");
        if (keys.HasFlag(Keys.Shift)) parts.Add("Shift");
        parts.Add(key is >= Keys.D0 and <= Keys.D9 ? ((int)(key - Keys.D0)).ToString() : key.ToString());
        return string.Join("+", parts);
    }
}
