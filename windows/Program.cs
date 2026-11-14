namespace AstromechDesk;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var instance = new Mutex(false, @"Local\AstromechDesk");
        if (!instance.WaitOne(0)) return;
        try
        {
            ApplicationConfiguration.Initialize();
            using var controls = new ControlPanel();
            Application.Run(controls.StartInTray());
        }
        finally { instance.ReleaseMutex(); }
    }
}
