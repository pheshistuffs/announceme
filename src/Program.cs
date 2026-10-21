using System.IO.Pipes;
using Microsoft.Win32;

namespace AnnounceMe;

static class Program
{
    const string MutexName = @"Local\AnnounceMe.SingleInstance";
    static readonly string PipeName = $"AnnounceMe.{Environment.UserName}";

    [STAThread]
    static void Main(string[] args)
    {
        // "announceme:..." links from notification buttons, or "show" when started again.
        var command = args.FirstOrDefault(a => a.StartsWith("announceme:", StringComparison.OrdinalIgnoreCase))
                      ?? (args.Contains("--quick") ? "announceme:quick" : "announceme:show");

        using var mutex = new Mutex(true, MutexName, out bool first);
        if (!first)
        {
            SendToRunning(command);
            return;
        }

        ApplicationConfiguration.Initialize();
        Store.Load();
        Protocol.Register();

        var tray = new TrayContext(startHidden: args.Contains("--tray") || command != "announceme:show");
        new Thread(() => ListenForCommands(tray)) { IsBackground = true }.Start();
        if (command != "announceme:show") tray.InvokeCommand(command);

        Application.Run(tray);
    }

    static void SendToRunning(string command)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            pipe.Connect(3000);
            using var w = new StreamWriter(pipe);
            w.WriteLine(command);
        }
        catch { }
    }

    static void ListenForCommands(TrayContext tray)
    {
        while (true)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly);
                pipe.WaitForConnection();
                using var r = new StreamReader(pipe);
                var line = r.ReadLine();
                if (!string.IsNullOrWhiteSpace(line)) tray.InvokeCommand(line.Trim());
            }
            catch
            {
                Thread.Sleep(500);
            }
        }
    }
}

// The announceme: link scheme, so buttons on notifications can reach the running app.
public static class Protocol
{
    public static void Register()
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\announceme");
            k.SetValue("", "URL:AnnounceMe");
            k.SetValue("URL Protocol", "");
            using var cmd = k.CreateSubKey(@"shell\open\command");
            cmd.SetValue("", $"\"{Environment.ProcessPath}\" \"%1\"");
        }
        catch { }
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
