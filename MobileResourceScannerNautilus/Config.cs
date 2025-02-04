using Nautilus.Json;
using Nautilus.Options.Attributes;

namespace MobileResourceScannerNautilus;

[Menu("Mobile Resource Scanner Options")]
public class Config : ConfigFile
{
    public Config() : base()
    {
        
    }

    [Toggle(Label = "Enable this mod")]
    public bool ModEnabled = true;

    [Toggle(Label = "Enable debug logs")]
    public bool IsDebug = true;

    [Toggle(Label = "Require Scanned", Tooltip = "Only show resources that have been scanned by the player")]
    public bool RequireScanned = false;

    [Slider(DefaultValue = 500, Min = 50, Max = 5000, Label = "Range (m)")]
    public float Range = 500;
        
    [Slider(DefaultValue = 1, Min = 0, Max = 1, Label = "Which button to use to open the menu.")]
    public int MenuButton = 1;

    [Slider(DefaultValue = 10, Min = 1, Max = 100, Label = "Interval (s)")]
    public float Interval = 10;

    //Current resource type to scan for
    public string CurrentResource = "None";

    [Keybind(Label = "Key shortcut used to open the menu (combined with SHIFT).")]
    public UnityEngine.KeyCode MenuHotKey = UnityEngine.KeyCode.L;

    public string NameString = "Mobile Resource Scanner";

    public string DescriptionString = "Equip to enable mobile resource scanning";

    public string MenuHeader = "Select Resource";

    public string OpenMenuString = "Switch Resource ({0})";
}