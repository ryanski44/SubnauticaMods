using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Nautilus.Assets;
using Nautilus.Assets.Gadgets;
using Nautilus.Assets.PrefabTemplates;
using Nautilus.Crafting;
using Nautilus.Handlers;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Image = UnityEngine.UI.Image;

namespace MobileResourceScannerNautilus;

[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
[BepInDependency("com.snmodding.nautilus")]
public class Plugin : BaseUnityPlugin
{
    public new static ManualLogSource Logger { get; private set; } = BepInEx.Logging.Logger.CreateLogSource("MobileResourceScannerInit");
    
    private static Assembly Assembly { get; } = Assembly.GetExecutingAssembly();

    public static Config ModOptions;
    
    public static float lastInterval = -1;

    private static TechType currentTechType = TechType.None;
    private static string currentTechName = "None";

    public static GameObject menuGO;
    private static TechType chipTechType = TechType.MapRoomHUDChip;
    private static readonly string idString = "MobileResourceScanner";
    
    public static void Dbgl(string str = "", LogLevel logLevel = LogLevel.Debug)
    {
        if (ModOptions == null || ModOptions.IsDebug)
            Logger.Log(logLevel, str);
    }

    public Plugin() : base()
    {
        // set project-scoped logger instance
        Logger = base.Logger;
    }

    private void Awake()
    {
        // Enhancing the mod - Set up our Mod Options
        ModOptions = OptionsPanelHandler.RegisterModOptions<Config>();
        
        // Initialize custom prefabs
        InitializePrefabs();

        // register harmony patches, if there are any
        Harmony.CreateAndPatchAll(Assembly, $"{PluginInfo.PLUGIN_GUID}");

        Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), Info.Metadata.GUID);

        if(Enum.TryParse(ModOptions.CurrentResource, false, out currentTechType))
        {
            currentTechName = Language.main.Get(currentTechType);
        }

        LoadChip();

        Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
    }

    public void Update()
    {
        if (ModOptions != null && ModOptions.ModEnabled && new KeyboardShortcut(ModOptions.MenuHotKey, new KeyCode[] { KeyCode.LeftShift}).IsDown())
        {
            ShowMenu();
        }
    }

    private static void LoadChip()
    {
        Dbgl($"Adding chip");

        var scannerModule = new CustomPrefab(
            idString,
            ModOptions.NameString,
            ModOptions.DescriptionString,
            SpriteManager.Get(TechType.MapRoomHUDChip));

        // Set our prefab to a clone of the MapRoomHUDChip
        scannerModule.SetGameObject(new CloneTemplate(scannerModule.Info, TechType.MapRoomHUDChip));

        // Make our item compatible with the player chip slot
        scannerModule.SetEquipment(EquipmentType.Chip)
            .WithQuickSlotType(QuickSlotType.None);

        // Make the map room a requirement for our item's blueprint
        ScanningGadget scanning = scannerModule.SetUnlock(TechType.BaseMapRoom);

        // Add our item to the map room upgrades category
        scanning.WithPdaGroupCategory(TechGroup.MapRoomUpgrades, TechCategory.MapRoomUpgrades);

        var recipe = new RecipeData()
        {
            craftAmount = 1,
            Ingredients =
            {
                new CraftData.Ingredient(TechType.ComputerChip, 1),
                new CraftData.Ingredient(TechType.Magnetite, 1),
            },
        };

        // Add a recipe for our item, as well as add it to the map room fabricator
        scannerModule.SetRecipe(recipe)
            .WithFabricatorType(CraftTree.Type.MapRoom)
            .WithCraftingTime(1);

        // Register our item to the game
        scannerModule.Register();

        Dbgl($"Added chip {chipTechType}");
    }

    [HarmonyPatch(typeof(uGUI_ResourceTracker), "IsVisibleNow")]
    private static class uGUI_ResourceTracker_IsVisibleNow_Patch
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            Dbgl("transpiling uGUI_ResourceTracker.IsVisibleNow");

            var codes = new List<CodeInstruction>(instructions);
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Callvirt && codes[i].operand is MethodInfo && (MethodInfo)codes[i].operand == AccessTools.Method(typeof(Equipment), nameof(Equipment.GetCount)))
                {
                    Dbgl("Found method");
                    codes.Insert(i + 1, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Plugin), nameof(Plugin.GetCount))));
                    break;
                }
            }

            return codes.AsEnumerable();
        }
    }

    private static int GetCount(int count)
    {
        if (ModOptions == null || !ModOptions.ModEnabled || count > 0)
            return count;
        return Inventory.main.equipment.GetCount(chipTechType);
    }

    [HarmonyPatch(typeof(uGUI_ResourceTracker), "GatherNodes")]
    private static class uGUI_ResourceTracker_GatherNodes_Patch
    {
        static bool Prefix(uGUI_ResourceTracker __instance, HashSet<ResourceTrackerDatabase.ResourceInfo> ___nodes, List<TechType> ___techTypes)
        {
            if (ModOptions == null || !ModOptions.ModEnabled || currentTechType == TechType.None || Inventory.main?.equipment?.GetCount(chipTechType) == 0)
                return true;

            Camera camera = MainCamera.camera;
            ___nodes.Clear();
            ___techTypes.Clear();
            ResourceTrackerDatabase.GetTechTypesInRange(camera.transform.position, ModOptions.Range, new List<TechType>() { currentTechType });
            ResourceTrackerDatabase.GetNodes(camera.transform.position, ModOptions.Range, currentTechType, ___nodes);

            if (lastInterval != ModOptions.Interval)
            {
                __instance.CancelInvoke("GatherNodes");
                __instance.InvokeRepeating("GatherNodes", ModOptions.Interval, ModOptions.Interval);
                lastInterval = ModOptions.Interval;
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(uGUI_Equipment), nameof(uGUI_Equipment.OnPointerClick))]
    private static class uGUI_Equipment_OnPointerClick_Patch
    {
        static bool Prefix(uGUI_Equipment __instance, uGUI_EquipmentSlot instance, int button, Dictionary<uGUI_EquipmentSlot, InventoryItem> ___slots)
        {
            if (ModOptions == null || !ModOptions.ModEnabled)
                return true;
            if(___slots.TryGetValue(instance, out var item))
            {
                if (item.techType.ToString() == idString)
                {
                    if(Inventory.main.GetItemAction(item, button) == ItemAction.None && button == ModOptions.MenuButton)
                    {
                        Dbgl("Clicked on chip, showing menu");
                        Player.main.GetPDA().Close();
                        ShowMenu();
                        return false;
                    }
                }
            }
            return true;
        }
    }
    
    [HarmonyPatch(typeof(TooltipFactory), "ItemActions")]
    private static class TooltipFactory_ItemActions_Patch
    {
        static void Postfix(StringBuilder sb, InventoryItem item)
        {
            if (ModOptions == null || !ModOptions.ModEnabled || item.techType != chipTechType)
                return;
            AccessTools.Method(typeof(TooltipFactory), "WriteAction").Invoke(null, new object[] { sb, AccessTools.Field(typeof(TooltipFactory), $"stringButton{ModOptions.MenuButton}").GetValue(null), string.Format(ModOptions.OpenMenuString, currentTechName) });
        }
    }

    private static void ShowMenu()
    {
        var template = IngameMenu.main?.GetComponentInChildren<IngameMenuTopLevel>();
        if (template is null)
        {
            ErrorMessage.AddWarning("Menu template not found!");
            return;
        }
        var rtt = IngameMenu.main.GetComponent<RectTransform>();
        menuGO = new GameObject("ResourceMenu");
        menuGO.transform.SetParent(uGUI.main.hud.transform);
        var rtb = menuGO.AddComponent<RectTransform>();
        rtb.sizeDelta = new Vector2(545, 700);
        //rtb.localPosition = rtt.localPosition;
        //rtb.pivot = rtt.pivot;
        //rtb.anchorMax = rtt.anchorMax;
        //rtb.anchorMin = rtt.anchorMin;

        Dbgl($"Adding scroll view");

        GameObject scrollObject = new GameObject() { name = "ScrollView" };
        scrollObject.transform.SetParent(menuGO.transform);
        var rts = scrollObject.AddComponent<RectTransform>();
        rts.sizeDelta = rtb.sizeDelta;

        GameObject mask = new GameObject { name = "Mask" };
        mask.transform.SetParent(scrollObject.transform);
        var rtm = mask.AddComponent<RectTransform>();
        rtm.sizeDelta = rtb.sizeDelta;

        Texture2D tex = new Texture2D((int)Mathf.Ceil(rtm.rect.width), (int)Mathf.Ceil(rtm.rect.height));
        Image image = mask.AddComponent<Image>();
        image.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), Vector2.zero);
        Mask m = mask.AddComponent<Mask>();
        m.showMaskGraphic = false;

        var sr = scrollObject.AddComponent<ScrollRect>();

        Dbgl("Added scroll view");

        var gridGO = Instantiate(template.gameObject, mask.transform);
        var header = gridGO.transform.Find("Header");
        var rth = header.GetComponent<RectTransform>();

        var rtg = gridGO.GetComponent<RectTransform>();
        sr.content = rtg;

        var menu = gridGO.AddComponent<ResourceMenu>();
        menu.Select();

        sr.movementType = ScrollRect.MovementType.Clamped;
        sr.horizontal = false;
        sr.viewport = mask.GetComponent<RectTransform>();
        sr.scrollSensitivity = 50;

        Dbgl("Created menu");

        var buttons = gridGO.GetComponentsInChildren<Button>(true);
        Button templateButton = null;
        bool first = true;
        foreach(var button in buttons)
        {
            if (first)
            {
                templateButton = button;
                Destroy(templateButton.gameObject.GetComponent<EventTrigger>());
                first = false;
            }
            else Destroy(button.gameObject);
        }
        var techs = new List<TechType>();
        foreach(var t in ResourceTrackerDatabase.GetTechTypes())
        {
            if (!ModOptions.RequireScanned || PDAScanner.ContainsCompleteEntry(t))
                techs.Add(t);
        }
        
        techs.Sort(delegate (TechType a, TechType b)
        {
            return Language.main.Get(a).CompareTo(Language.main.Get(b));
        });

        Dbgl($"Found {techs.Count} techs");
        if(techs.Count < 1)
        {
            ErrorMessage.AddWarning("No techs found!");
            return;
        }

        techs.Insert(0, TechType.None);

        rtb.localScale = Vector3.one;
        rtb.anchoredPosition3D = Vector3.zero;


        header.SetParent(menuGO.transform);
        rth.anchoredPosition = new Vector2(272.5f, 700);
        rth.sizeDelta = new Vector2(545f, 100);
        var headerText = header.GetComponent<TextMeshProUGUI>();
        headerText.text = ModOptions.MenuHeader;
        Dbgl($"Header size: {rth.sizeDelta}");

        foreach (var t in techs)
        {
            GameObject b = Instantiate(templateButton.gameObject, gridGO.transform);
            SetupButton(b, t);
        }
        Destroy(templateButton.gameObject);

        sr.verticalNormalizedPosition = 1;
        sr.horizontalNormalizedPosition = 0;

        uGUI_INavigableIconGrid grid = gridGO.GetComponentInChildren<uGUI_INavigableIconGrid>();
        if(grid is null)
            grid = gridGO.GetComponent<uGUI_INavigableIconGrid>();
        if(grid != null)
            GamepadInputModule.current.SetCurrentGrid(grid);
    }

    private static void SetupButton(GameObject gameObject, TechType t)
    {
        gameObject.name = $"Button{t}";
        var button = gameObject.GetComponent<Button>();
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(delegate ()
        {
            currentTechType = t;
            ModOptions.CurrentResource = t.ToString();
            currentTechName = Language.main.Get(t);
            ErrorMessage.AddWarning($"Mobile scanner tech type set to {currentTechName}");
            Destroy(menuGO);
        });
        var text = gameObject.transform.GetComponentInChildren<TextMeshProUGUI>();
        text.text = Language.main.Get(t);
    }

    private void InitializePrefabs()
    {

    }
}