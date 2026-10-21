using Microsoft.Win32;

namespace AnnounceMe;

static class Program
{
    const string MutexName = @"Local\AnnounceMe.SingleInstance";
    const string ShowEventName = @"Local\AnnounceMe.Show";

    [STAThread]
    static void Main(string[] args)
    {
        using var mutex = new Mutex(true, MutexName, out bool first);
        if (!first)
        {
            // Already running: ask that instance to show its window.
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
            return;
        }

        ApplicationConfiguration.Initialize();
        Store.Load();

        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var tray = new TrayContext(startHidden: args.Contains("--tray"));
        var waiter = new Thread(() =>
        {
            while (showEvent.WaitOne())
                tray.InvokeShowMain();
        }) { IsBackground = true };
        waiter.Start();

        Application.Run(tray);
    }
}

public static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "AnnounceMe";

    public static bool Enabled
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(ValueName) is string;
        }
        set
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) k.SetValue(ValueName, $"\"{Environment.ProcessPath}\" --tray");
            else k.DeleteValue(ValueName, false);
        }
    }

    // Keeps the autostart entry pointing at the current exe if the user moved it.
    public static void Refresh()
    {
        if (Enabled) Enabled = true;
    }
}
