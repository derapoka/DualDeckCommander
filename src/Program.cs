using System.Text;

namespace DualDeckCommander;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Régi kódlapok (pl. Windows-1250) a nézőkéhez
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
            MessageBox.Show(e.Exception.Message, "Hiba", MessageBoxButtons.OK, MessageBoxIcon.Error);

        var settings = Settings.Load();
        Settings.Current = settings;

        // Indítóképernyő (kikapcsolható; a /nosplash kapcsoló is kihagyja)
        bool noSplash = args.Any(a => a.Equals("/nosplash", StringComparison.OrdinalIgnoreCase));
        SplashForm? splash = null;
        if (settings.ShowSplash && !noSplash)
        {
            splash = new SplashForm();
            splash.Show();
            splash.Update();
        }

        // Megnyitandó mappa / fájl a parancssorból (pl. fájltársításból: DualDeckCommander.exe "D:\játék.adf")
        string? startPath = args.FirstOrDefault(a => !a.StartsWith('/'));

        splash?.SetStatus("Felület felépítése…");
        var main = new MainForm(settings, startPath);
        if (splash != null)
        {
            main.Shown += (_, _) => splash.CloseWhenReady(main);
            main.FormClosed += (_, _) => { if (!splash.IsDisposed) splash.Close(); };
        }
        Application.Run(main);
    }
}
