using System.Windows;

namespace D3Offline.CombatMeter;

public static class App
{
    [STAThread]
    public static void Main(string[] args)
    {
        using var instance = new Mutex(true, "Local\\D3Offline.CombatMeter", out var first);
        if (!first) return;
        var application = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        application.Run(new MeterWindow(args));
    }
}
