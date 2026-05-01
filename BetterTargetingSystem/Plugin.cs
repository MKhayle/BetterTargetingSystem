using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.Group;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using BetterTargetingSystem.Windows;
using System;
using System.Collections.Generic;
using System.Linq;


using Dalamud.Plugin.Services;

using DalamudCharacter = Dalamud.Game.ClientState.Objects.Types.ICharacter;
using DalamudGameObject = Dalamud.Game.ClientState.Objects.Types.IGameObject;
using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;
using GameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using Dalamud.Bindings.ImGui;

namespace BetterTargetingSystem;

public sealed unsafe class Plugin : IDalamudPlugin
{
    public string Name => "Better Targeting System";
    public string CommandConfig => "/bts";
    public string CommandHelp => "/btshelp";

    internal IEnumerable<uint> LastConeTargets { get; private set; } = Enumerable.Empty<uint>();
    internal List<uint> CyclingTargets { get; private set; } = new List<uint>();
    internal DebugMode DebugMode { get; private set; }

    [PluginService] public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] public static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] public static IFramework Framework { get; set; }
    [PluginService] public static IPluginLog PluginLog { get; set; }
    public Configuration Configuration { get; init; }

    [PluginService] public static IClientState Client { get; set; } = null!;
    [PluginService] public static IObjectTable ObjectTable { get; set; } = null!;
    [PluginService] public static ITargetManager TargetManager { get; set; } = null!;
    [PluginService] public static IGameGui GameGui { get; set; } = null!;
    [PluginService] public static IGameInteropProvider GameInteropProvider { get; set; } = null!;
    [PluginService] public static IKeyState KeyState { get; set; } = null!;
    [PluginService] public static ICondition Condition { get; set; } = null!;

    private ConfigWindow ConfigWindow { get; init; }
    private HelpWindow HelpWindow { get; init; }
    private WindowSystem WindowSystem = new("BetterTargetingSystem");

    // Shamelessly stolen, not sure what that game function exactly does but it works
    //[Signature("48 89 5C 24 ?? 57 48 83 EC 20 48 8B DA 8B F9 E8 ?? ?? ?? ?? 4C 8B C3")]
    //internal static CanAttackDelegate? CanAttackFunction = null!;
    //internal delegate nint CanAttackDelegate(nint a1, nint objectAddress);

    public Plugin()
    {
        GameInteropProvider.InitializeFromAttributes(this);


        this.Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        this.Configuration.Initialize(PluginInterface);

        Framework.Update += Update;
        Client.TerritoryChanged += ClearLists;

        ConfigWindow = new ConfigWindow(this);
        WindowSystem.AddWindow(ConfigWindow);
        HelpWindow = new HelpWindow(this);
        WindowSystem.AddWindow(HelpWindow);

        this.DebugMode = new DebugMode(this);
        PluginInterface.UiBuilder.Draw += DrawUI;
        PluginInterface.UiBuilder.OpenMainUi += DrawHelpUI;
        PluginInterface.UiBuilder.OpenConfigUi += DrawConfigUI;

        CommandManager.AddHandler(CommandConfig, new CommandInfo(ShowConfigWindow)
            { HelpMessage = "Open the configuration window." });
        CommandManager.AddHandler(CommandHelp, new CommandInfo(ShowHelpWindow)
            { HelpMessage = "What does this plugin do?" });
    }

    public void Dispose()
    {
        Framework.Update -= Update;
        Client.TerritoryChanged -= ClearLists;
        CommandManager.RemoveHandler(CommandConfig);
        CommandManager.RemoveHandler(CommandHelp);
        this.WindowSystem.RemoveAllWindows();
        ConfigWindow.Dispose();
        HelpWindow.Dispose();
    }

    public void Log(string message) => PluginLog.Debug(message);
    private void DrawUI() => this.WindowSystem.Draw();
    private void DrawHelpUI() => HelpWindow.Toggle();
    private void DrawConfigUI() => ConfigWindow.Toggle();
    private void ShowHelpWindow(string command, string args) => this.DrawHelpUI();
    private void ShowConfigWindow(string command, string args) => this.DrawConfigUI();

    public void ClearLists(uint territoryType)
    {
        // Attempt to fix a very rare bug I can't reproduce
        this.LastConeTargets = new List<uint>();
        this.CyclingTargets = new List<uint>();
    }

    public void Update(IFramework framework)
    {
        if (Client.IsLoggedIn == false || ObjectTable.LocalPlayer == null)
            return;

        // Disable features in PvP
        if (Client.IsPvP)
            return;

        // Disable in GPose
        if (Client.IsGPosing)
            return;

        // Disable if keyboard is being used to type text
        if (Utils.IsTextInputActive || ImGui.GetIO().WantCaptureKeyboard)
            return;

        Keybinds.Keybind.GetKeyboardState();

        if (Configuration.TabTargetKeybind.IsPressed())
        {
            try { KeyState[(int)Configuration.TabTargetKeybind.Key!] = false; } catch { }
            CycleTargets();
            return;
        }

        if (Configuration.ClosestTargetKeybind.IsPressed())
        {
            try { KeyState[(int)Configuration.ClosestTargetKeybind.Key!] = false; } catch { }
            TargetClosest();
            return;
        }

        if (Configuration.LowestHealthTargetKeybind.IsPressed())
        {
            try { KeyState[(int)Configuration.LowestHealthTargetKeybind.Key!] = false; } catch { }
            TargetLowestHealth();
            return;
        }

        if (Configuration.BestAOETargetKeybind.IsPressed())
        {
            try { KeyState[(int)Configuration.BestAOETargetKeybind.Key!] = false; } catch { }
            TargetBestAOE();
            return;
        }
    }

    private void SetTarget(DalamudGameObject target)
    {
        TargetManager.SoftTarget = null;
        TargetManager.Target = target;
    }

    private void TargetLowestHealth() => TargetClosest(true);

    private void TargetClosest(bool lowestHealth = false)
    {
        if (ObjectTable.LocalPlayer == null)
            return;

        var (Targets, CloseTargets, EnemyListTargets, OnScreenTargets) = GetTargets();

        // All objects in Targets and CloseTargets are in OnScreenTargets so it's not necessary to test them
        if (EnemyListTargets.Count == 0 && OnScreenTargets.Count == 0)
            return;

        var _targets = OnScreenTargets.Count > 0 ? OnScreenTargets : EnemyListTargets;

        var _target = lowestHealth
            ? _targets.OrderBy(o => (o as DalamudCharacter)?.CurrentHp).ThenBy(o => Utils.DistanceBetweenObjects(ObjectTable.LocalPlayer, o)).First()
            : _targets.OrderBy(o => Utils.DistanceBetweenObjects(ObjectTable.LocalPlayer, o)).First();

        SetTarget(_target);
    }

    private class AOETarget
    {
        public DalamudGameObject obj;
        public int inRange = 0;
        public AOETarget(DalamudGameObject obj) => this.obj = obj;
    }
    private void TargetBestAOE()
    {
        if (ObjectTable.LocalPlayer == null)
            return;

        var (Targets, CloseTargets, EnemyListTargets, OnScreenTargets) = GetTargets();

        if (OnScreenTargets.Count == 0)
            return;

        var groupManager = GroupManager.Instance();
        if (groupManager != null)
        {
            EnemyListTargets.AddRange(OnScreenTargets.Where(o =>
                EnemyListTargets.Contains(o) == false
                && ((o as DalamudCharacter)?.StatusFlags & StatusFlags.InCombat) != 0
                && groupManager->MainGroup.GetPartyMemberByEntityId((uint)o.TargetObjectId) != null
            ));
        }

        if (EnemyListTargets.Count == 0)
            return;

        var AOETargetsList = new List<AOETarget>();
        foreach (var enemy in EnemyListTargets)
        {
            var AOETarget = new AOETarget(enemy);
            foreach (var other in EnemyListTargets)
            {
                if (other == enemy) continue;
                if (Utils.DistanceBetweenObjects(enemy, other) > 5) continue;
                AOETarget.inRange += 1;
            }
            AOETargetsList.Add(AOETarget);
        }

        var _targets = AOETargetsList.Where(o => OnScreenTargets.Contains(o.obj)).ToList();

        if (_targets.Count == 0)
            return;

        var _target = _targets.OrderByDescending(o => o.inRange).ThenByDescending(o => (o.obj as DalamudCharacter)?.CurrentHp).First().obj;

        SetTarget(_target);
    }

    private void CycleTargets()
    {
        if (ObjectTable.LocalPlayer == null)
            return;

        var (Targets, CloseTargets, EnemyListTargets, OnScreenTargets) = GetTargets();

        // All objects in Targets and CloseTargets are in OnScreenTargets so it's not necessary to test them
        if (EnemyListTargets.Count == 0 && OnScreenTargets.Count == 0)
            return;

        var _currentTarget = TargetManager.Target;
        var _previousTarget = TargetManager.PreviousTarget;
        var _targetObjectId = _currentTarget?.EntityId ?? _previousTarget?.EntityId ?? 0;

        // Targets in the frontal cone
        if (Targets.Count > 0)
        {
            Targets = Targets.OrderBy(o => Utils.DistanceBetweenObjects(ObjectTable.LocalPlayer, o)).ToList();

            var TargetsObjectIds = Targets.Select(o => o.EntityId);
            // Same cone targets as last cycle
            if (this.LastConeTargets.ToHashSet().SetEquals(TargetsObjectIds.ToHashSet()))
            {
                // Add the close targets to the list of potential targets
                var _potentialTargets = Targets.UnionBy(CloseTargets, o => o.EntityId).ToList();
                var _potentialTargetsObjectIds = _potentialTargets.Select(o => o.EntityId);

                // New enemies to be added
                if (_potentialTargetsObjectIds.Any(o => this.CyclingTargets.Contains(o) == false))
                    this.CyclingTargets = this.CyclingTargets.Union(_potentialTargetsObjectIds).ToList();

                // We simply select the next target
                this.CyclingTargets = this.CyclingTargets.Intersect(_potentialTargetsObjectIds).ToList();
                var index = this.CyclingTargets.FindIndex(o => o == _targetObjectId);
                if (index == this.CyclingTargets.Count - 1) index = -1;
                SetTarget(_potentialTargets.Find(o => o.EntityId == this.CyclingTargets[index + 1]));
            }
            else
            {
                var _potentialTargets = Targets;
                var _potentialTargetsObjectIds = _potentialTargets.Select(o => o.EntityId).ToList();
                var index = _potentialTargetsObjectIds.FindIndex(o => o == _targetObjectId);
                if (index == _potentialTargetsObjectIds.Count - 1) index = -1;
                SetTarget(_potentialTargets.Find(o => o.EntityId == _potentialTargetsObjectIds[index + 1]));

                this.LastConeTargets = TargetsObjectIds;
                this.CyclingTargets = _potentialTargetsObjectIds;
            }

            return;
        }

        this.LastConeTargets = Enumerable.Empty<uint>();

        if (CloseTargets.Count > 0)
        {
            var _potentialTargetsObjectIds = CloseTargets.Select(o => o.EntityId);

            if (_potentialTargetsObjectIds.Any(o => this.CyclingTargets.Contains(o) == false))
                this.CyclingTargets = this.CyclingTargets.Union(_potentialTargetsObjectIds).ToList();

            // We simply select the next target
            this.CyclingTargets = this.CyclingTargets.Intersect(_potentialTargetsObjectIds).ToList();
            var index = this.CyclingTargets.FindIndex(o => o == _targetObjectId);
            if (index == this.CyclingTargets.Count - 1) index = -1;
            SetTarget(CloseTargets.Find(o => o.EntityId == this.CyclingTargets[index + 1]));

            return;
        }

        if (EnemyListTargets.Count > 0)
        {
            var _potentialTargetsObjectIds = EnemyListTargets.Select(o => o.EntityId);

            if (_potentialTargetsObjectIds.Any(o => this.CyclingTargets.Contains(o) == false))
                this.CyclingTargets = this.CyclingTargets.Union(_potentialTargetsObjectIds).ToList();

            // We simply select the next target
            this.CyclingTargets = this.CyclingTargets.Intersect(_potentialTargetsObjectIds).ToList();
            var index = this.CyclingTargets.FindIndex(o => o == _targetObjectId);
            if (index == this.CyclingTargets.Count - 1) index = -1;
            SetTarget(EnemyListTargets.Find(o => o.EntityId == this.CyclingTargets[index + 1]));

            return;
        }

        if (OnScreenTargets.Count > 0)
        {
            OnScreenTargets = OnScreenTargets.OrderBy(o => Utils.DistanceBetweenObjects(ObjectTable.LocalPlayer, o)).ToList();
            var _potentialTargetsObjectIds = OnScreenTargets.Select(o => o.EntityId);

            if (_potentialTargetsObjectIds.Any(o => this.CyclingTargets.Contains(o) == false))
                this.CyclingTargets = this.CyclingTargets.Union(_potentialTargetsObjectIds).ToList();

            // We simply select the next target
            this.CyclingTargets = this.CyclingTargets.Intersect(_potentialTargetsObjectIds).ToList();
            var index = this.CyclingTargets.FindIndex(o => o == _targetObjectId);
            if (index == this.CyclingTargets.Count - 1) index = -1;
            SetTarget(OnScreenTargets.Find(o => o.EntityId == this.CyclingTargets[index + 1]));
        }
    }

    public record ObjectsList(List<DalamudGameObject> Targets, List<DalamudGameObject> CloseTargets, List<DalamudGameObject> TargetsEnemy, List<DalamudGameObject> OnScreenTargets);
    internal ObjectsList GetTargets()
    {
        /* Always return 4 lists.
         * The enemies in a cone in front of the player
         * The enemies in a close radius around the player
         * The enemies in the Enemy List Addon
         * All the targets on screen
         */
        var TargetsList = new List<DalamudGameObject>();
        var CloseTargetsList = new List<DalamudGameObject>();
        var TargetsEnemyList = new List<DalamudGameObject>();
        var OnScreenTargetsList = new List<DalamudGameObject>();

        var Player = ObjectTable.LocalPlayer != null ? (GameObject*)ObjectTable.LocalPlayer.Address : null;
        if (Player == null)
            return new ObjectsList(TargetsList, CloseTargetsList, TargetsEnemyList, OnScreenTargetsList);

        // There might be a way to store this and just update the values if they actually change
        var device = Device.Instance();
        float deviceWidth = device->Width;
        float deviceHeight = device->Height;

        var PotentialTargets = ObjectTable.Where(
            o => (ObjectKind.BattleNpc.Equals(o.ObjectKind)
                  || ObjectKind.Pc.Equals(o.ObjectKind))
                 && o != ObjectTable.LocalPlayer
                 && Utils.CanAttack(o)
        );

        var EnemyList = Utils.GetEnemyListObjectIds();

        foreach (var obj in PotentialTargets)
        {
            // In the enemy list addon, adding it to the Enemy list
            if (EnemyList.Contains(obj.EntityId))
                TargetsEnemyList.Add(obj);

            var o = (GameObject*)obj.Address;
            if (o == null) continue;

            if (o->GetIsTargetable() == false) continue;

            // If the object is part of another party's treasure hunt/leve, we ignore it
            if ((o->EventId.ContentId == EventHandlerContent.TreasureHuntDirector || o->EventId.ContentId == EventHandlerContent.BattleLeveDirector)
                && o->EventId.Id != Player->EventId.Id)
                continue;

            var distance = Utils.DistanceBetweenObjects(ObjectTable.LocalPlayer!, obj);

            // This is a bit less than the max distance to target something the vanilla way
            if (distance > 49) continue;

            /*
             * Check if object is visible on screen or not.
             * Using both WorldToScreenPoint and WorldToScreen because
             *  - the former is more accurate for actual X,Y position on screen
             *  - the latter returns whether or not the object is "in front" of the camera
             * This isn't exactly how I'd like to do it but since I couldn't find how to get
             * the "bounding box" of a game object or the dimensions of its model, this will have to do.
             */
            FFXIVClientStructs.FFXIV.Common.Math.Vector2 screenPos = new();
            FFXIVClientStructs.FFXIV.Common.Math.Vector3 worldPos = o->Position;
            FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Camera.WorldToScreenPoint(&screenPos, &worldPos);
            if (screenPos.X < 0
                || screenPos.X > deviceWidth
                || screenPos.Y < 0
                || screenPos.Y > deviceHeight) continue;
            if (GameGui.WorldToScreen(o->Position, out _) == false) continue;

            // Check actual line of sight from camera to object (blocked by walls, etc)
            if (Utils.IsInLineOfSight(o, true) == false) continue;

            // On screen and in light of sight of the camera, adding it to the On Screen list
            OnScreenTargetsList.Add(obj);

            // Close to the player, adding it to the Close targets list
            if (Configuration.CloseTargetsCircleEnabled && distance < Configuration.CloseTargetsCircleRadius)
                CloseTargetsList.Add(obj);

            // Further than the bigger cone, don't care about targeting it
            if (Configuration.Cone3Enabled)
            {
                if (distance > Configuration.Cone3Distance)
                    continue;
            }
            else if (Configuration.Cone2Enabled)
            {
                if (distance > Configuration.Cone2Distance)
                    continue;
            }
            else if (distance > Configuration.Cone1Distance)
                continue;

            // Default cone angle for very close targets, getting wider the closer the target is
            var angle = Configuration.Cone1Angle;
            if (Configuration.Cone3Enabled)
            {
                if (Configuration.Cone2Enabled)
                {
                    if (distance > Configuration.Cone2Distance)
                        angle = Configuration.Cone3Angle;
                    else if (distance > Configuration.Cone1Distance)
                        angle = Configuration.Cone2Angle;
                }
                else if (distance > Configuration.Cone1Distance)
                    angle = Configuration.Cone3Angle;
            }
            else if (Configuration.Cone2Enabled && distance > Configuration.Cone1Distance)
                angle = Configuration.Cone2Angle;

            if (Utils.IsInFrontOfCamera(obj, angle) == false) continue;

            // In front of the player, adding it to the default list
            TargetsList.Add(obj);
        }

        return new ObjectsList(TargetsList, CloseTargetsList, TargetsEnemyList, OnScreenTargetsList);
    }
}
