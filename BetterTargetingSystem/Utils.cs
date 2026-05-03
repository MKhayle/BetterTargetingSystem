using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Common.Component.BGCollision;
using System;
using System.Numerics;
using DalamudGameObject = Dalamud.Game.ClientState.Objects.Types.IGameObject;
using GameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;
using CameraManager = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.CameraManager;
using CSFramework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework;
using System.Collections.Generic;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Arrays;
using Serilog;

namespace BetterTargetingSystem;

public unsafe class Utils
{
    private static RaptureAtkModule* RaptureAtkModule => CSFramework.Instance()->GetUIModule()->GetRaptureAtkModule();
    internal static bool IsTextInputActive => RaptureAtkModule->AtkModule.IsTextInputActive();

    internal static bool CanAttack(DalamudGameObject obj)
    {
        return ActionManager.CanUseActionOnTarget(142, (GameObject*)obj.Address);
    }

    internal static float DistanceBetweenObjects(DalamudGameObject source, DalamudGameObject target)
    {
        return DistanceBetweenObjects(source.Position, target.Position, target.HitboxRadius);
    }
    internal static float DistanceBetweenObjects(Vector3 sourcePos, Vector3 targetPos, float targetHitboxRadius = 0)
    {
        // Might have to tinker a bit whether or not to include hitbox radius in calculation
        // Keeping the source object hitbox radius outside of the calculation for now
        var distance = Vector3.Distance(sourcePos, targetPos);
        //distance -= source.HitboxRadius;
        distance -= targetHitboxRadius;
        Plugin.Log($"Distance: {distance} / Source position: {sourcePos} / Target position: {targetPos}");
        return distance;
    }



    internal unsafe static float GetCameraRotation()
    {
        var cameraManager = CameraManager.Instance();
        if (cameraManager == null || cameraManager->CurrentCamera == null)
            return 0;

        // Extract horizontal rotation from ViewMatrix
        var matrix = cameraManager->CurrentCamera->ViewMatrix;
        return (float)Math.Atan2(matrix.M31, matrix.M33);
    }

    internal unsafe static bool IsInFrontOfCamera(DalamudGameObject obj, float maxAngle)
    {
        var cameraManager = CameraManager.Instance();
        if (cameraManager == null || cameraManager->CurrentCamera == null || Plugin.ObjectTable.LocalPlayer == null)
            return false;

        var matrix = cameraManager->CurrentCamera->ViewMatrix;
        var p = obj.Position;
        
        // Transform world position to camera-space position (Projection)
        float camX = matrix.M11 * p.X + matrix.M21 * p.Y + matrix.M31 * p.Z + matrix.M41;
        float camZ = matrix.M13 * p.X + matrix.M23 * p.Y + matrix.M33 * p.Z + matrix.M43;
        
        // Horizontal angle in camera-space: atan2(X, -Z)
        var angle = Math.Abs(Math.Atan2(camX, -camZ));
        return angle <= Math.PI * maxAngle / 360;
    }

    internal static bool IsInLineOfSight(GameObject* target, bool useCamera = false)
    {
        var framework = CSFramework.Instance();
        if (framework == null || framework->BGCollisionModule == null)
        {
            Plugin.Log($"Framework is null. Returning false for {target->Name.ToString()}.");
            return false;
        }

        var sourcePos = FFXIVClientStructs.FFXIV.Common.Math.Vector3.Zero;
        if (useCamera)
        {
            // Using the camera's position as origin for raycast
            var cameraManager = CameraManager.Instance();
            if (cameraManager == null || cameraManager->CurrentCamera == null)
            {
                Plugin.Log($"Camera is null. Returning false for {target->Name.ToString()}.");
                return false;
            }

            sourcePos = cameraManager->CurrentCamera->Object.Position;
        }
        else
        {
            // Using player's position as origin for raycast
            if (Plugin.ObjectTable.LocalPlayer == null) return false;
            var player = (GameObject*)Plugin.ObjectTable.LocalPlayer.Address;
            if (player == null)
            {
                Plugin.Log($"Player is null. Returning false for {target->Name.ToString()}.");
                return false;
            }

            sourcePos = player->Position;
            sourcePos.Y += 2;
        }

        var targetPos = target->Position;
        targetPos.Y += 2;

        var direction = targetPos - sourcePos;
        var distance = direction.Magnitude;

        direction = direction.Normalized;

        System.Numerics.Vector3 originVect = new System.Numerics.Vector3(sourcePos.X, sourcePos.Y, sourcePos.Z);
        System.Numerics.Vector3 directionVect = new System.Numerics.Vector3(direction.X, direction.Y, direction.Z);

        RaycastHit hit;
        var flags = stackalloc int[] { 0x4000, 0, 0x4000, 0 };
        var isLoSBlocked = framework->BGCollisionModule->RaycastMaterialFilter(&hit, &originVect, &directionVect, distance, 1, flags);
        Plugin.Log($"LoS: {!isLoSBlocked}");
        return isLoSBlocked == false;
    }

    internal static uint[] GetEnemyListObjectIds()
    {
        var addonByName = Plugin.GameGui.GetAddonByName("_EnemyList", 1);
        if (addonByName == IntPtr.Zero)
            return Array.Empty<uint>();

        var addon = (AddonEnemyList*)addonByName.Address;
        var numArray = RaptureAtkModule->AtkModule.AtkArrayDataHolder.NumberArrays[21];
        var list = new List<uint>(addon->EnemyCount);
        for (var i = 0; i < addon->EnemyCount; i++)
        {
            var id = (uint)numArray->IntArray[8 + (i * 6)];
            list.Add(id);
        }
        return list.ToArray();
    }
}
