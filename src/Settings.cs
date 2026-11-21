using System.Text.Json;

namespace DualDeckCommander;

/// <summary>Beállítások, a %APPDATA%\DualDeckCommander\settings.json fájlban.</summary>
internal sealed class Settings
{
    public string LeftPath { get; set; } = "";
    public string RightPath { get; set; } = "";
    public bool ShowHidden { get; set; }
    public string Editor { get; set; } = "notepad.exe";
    /// <summary>"system", "light" vagy "dark".</summary>
    public string ThemeName { get; set; } = "system";
    public int LeftSortColumn { get; set; }
    public bool LeftSortDescending { get; set; }
    public int RightSortColumn { get; set; }
    public bool RightSortDescending { get; set; }
    public int WindowX { get; set; } = -1;
    public int WindowY { get; set; } = -1;
    public int WindowWidth { get; set; } = 1100;
    public int WindowHeight { get; set; } = 700;
    public bool Maximized { get; set; }
    public double SplitRatio { get; set; } = 0.5;
    public List<string> CommandHistory { get; set; } = new();
    public List<string> LeftTabs { get; set; } = new();
    public int LeftTabIndex { get; set; }
    public List<string> RightTabs { get; set; } = new();
    public int RightTabIndex { get; set; }
    public List<FavoriteDir> Favorites { get; set; } = new();

    // ---- Megjelenés ----
    /// <summary>A fájllisták betűmérete pontban (8, 9, 10.5, 12).</summary>
    public float FontSize { get; set; } = 9f;
    /// <summary>"compact", "normal" vagy "roomy".</summary>
    public string RowSpacing { get; set; } = "normal";
    public bool ShowToolbar { get; set; } = true;
    public bool ShowFunctionKeys { get; set; } = true;
    public bool ShowCommandLine { get; set; } = true;
    /// <summary>"short" (1,2 MB) vagy "bytes" (1 234 567).</summary>
    public string SizeFormat { get; set; } = "bytes";

    // ---- Működés ----
    /// <summary>"select" (Total Commander) vagy "menu" (Windows).</summary>
    public string RightClickMode { get; set; } = "select";
    public bool ConfirmDelete { get; set; } = true;
    public bool UseRecycleBin { get; set; } = true;
    /// <summary>"ask", "browse" vagy "mount".</summary>
    public string IsoEnterAction { get; set; } = "ask";
    /// <summary>"browse" (belenézés) vagy "shell" (megnyitás a Windows társított programjával).</summary>
    public string ArchiveEnterAction { get; set; } = "browse";
    public bool RememberTabs { get; set; } = true;
    public bool ShowSplash { get; set; } = true;
    /// <summary>"internal" (beépített képnézegető) vagy "shell" (a Windows társított programja).</summary>
    public string ImageOpenMode { get; set; } = "internal";

    // ---- Xbox GoD átalakítás ----
    /// <summary>Az utoljára használt GoD célmappa (üres = a másik panel mappája).</summary>
    public string GodOutputDir { get; set; } = "";
    public bool GodTrim { get; set; } = true;
    public bool GodShowResult { get; set; } = true;

    /// <summary>A futó program beállításai (a fájlpanelek innen olvassák).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public static Settings Current { get; set; } = new();

    public Settings Clone() => (Settings)MemberwiseClone();

    /// <summary>A "Működés" és "Megjelenés" beállítások alapértékre állítása (útvonalak, kedvencek maradnak).</summary>
    public void ResetOptions()
    {
        var d = new Settings();
        ThemeName = d.ThemeName; ShowHidden = d.ShowHidden; Editor = d.Editor;
        FontSize = d.FontSize; RowSpacing = d.RowSpacing; ShowToolbar = d.ShowToolbar;
        ShowFunctionKeys = d.ShowFunctionKeys; ShowCommandLine = d.ShowCommandLine; SizeFormat = d.SizeFormat;
        RightClickMode = d.RightClickMode; ConfirmDelete = d.ConfirmDelete; UseRecycleBin = d.UseRecycleBin;
        IsoEnterAction = d.IsoEnterAction; ArchiveEnterAction = d.ArchiveEnterAction;
        RememberTabs = d.RememberTabs; ShowSplash = d.ShowSplash; ImageOpenMode = d.ImageOpenMode;
    }

    public static string FolderPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DualDeckCommander");

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DualDeckCommander", "settings.json");

    /// <summary>A korábbi (TwinCommander nevű) verzió beállításai.</summary>
    private static string OldFilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TwinCommander", "settings.json");

    public static Settings Load()
    {
        try
        {
            if (!File.Exists(FilePath) && File.Exists(OldFilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(OldFilePath)) ?? new Settings();
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch
        {
            // sérült fájl esetén alapértékek
        }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // nem kritikus
        }
    }
}

/// <summary>Kedvenc mappa (Ctrl+D).</summary>
internal sealed class FavoriteDir
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
}
