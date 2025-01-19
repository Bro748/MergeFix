using Mono.Cecil.Cil;
using MonoMod.Cil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MergeFix;

internal static class OtherFixes
{
    public static void ApplyHooks()
    {
        On.RoomCamera.MoveCamera2 += RoomCamera_MoveCamera2;
        On.RoomCamera.MoveCamera_Room_int += RoomCamera_MoveCamera_Room_int;
        IL.RegionState.AdaptWorldToRegionState += RegionState_AdaptRegionStateToWorld;
        On.DeathFallGraphic.InitiateSprites += DeathFallGraphic_InitiateSprites;
        On.WaterLight.NewRoom += WaterLight_NewRoom;
        IL.WaterLight.NewRoom += WaterLight_NewRoom1;
        On.ShortcutGraphics.GenerateSprites += ShortcutGraphics_GenerateSprites;
        On.Menu.MultiplayerMenu.PopulateSafariButtons += MultiplayerMenu_PopulateSafariButtons;
    }


    private static void MultiplayerMenu_PopulateSafariButtons(On.Menu.MultiplayerMenu.orig_PopulateSafariButtons orig, Menu.MultiplayerMenu self)
    {
        if (self.GetGameTypeSetup.safariID >= ExtEnum<MultiplayerUnlocks.SafariUnlockID>.values.entries.Count)
        { self.GetGameTypeSetup.safariID = 0; }
        orig(self);
    }

    private static void ShortcutGraphics_GenerateSprites(On.ShortcutGraphics.orig_GenerateSprites orig, ShortcutGraphics self)
    {
        orig(self);
        if (!ModManager.MMF || !MoreSlugcats.MMF.cfgShowUnderwaterShortcuts.Value)
        { return; }

        for (int l = 0; l < self.room.shortcuts.Length; l++)
        {
            if (self.entranceSprites[l, 0] != null)
            {
                self.camera.ReturnFContainer("GrabShaders").AddChild(self.entranceSprites[l, 1]);
            }
        }
    }

    private static void WaterLight_NewRoom1(ILContext il)
    {
        try
        {
            var c = new ILCursor(il);
            c.TryGotoNext(MoveType.After, x => x.MatchLdfld<Room>(nameof(Room.roomRain)));
            c.EmitDelegate((RoomRain _) => true);
        }
        catch (Exception e)
        {
            MergeFixPlugin.BepLog("failed to il hook WaterLight_NewRoom\n" + e.ToString());
        }
    }

    private static void WaterLight_NewRoom(On.WaterLight.orig_NewRoom orig, WaterLight self, Water waterObject)
    {
        if (waterObject.room != null && !Futile.atlasManager.DoesContainElementWithName("RainMask_" + waterObject.room.abstractRoom.name))
        {
            UnityEngine.Debug.Log("adding fake RoomRain for WaterLight");
            waterObject.room.roomRain = CreateFakeRoomRain(waterObject.room);
        }
        orig(self, waterObject);
    }

    private static void DeathFallGraphic_InitiateSprites(On.DeathFallGraphic.orig_InitiateSprites orig, DeathFallGraphic self, RoomCamera.SpriteLeaser sLeaser, RoomCamera rCam)
    {
        if (!Futile.atlasManager.DoesContainElementWithName("RainMask_" + self.room.abstractRoom.name))
        {
            CreateFakeRoomRain(self.room);
        }
        orig(self, sLeaser, rCam);
    }

    private static RoomRain CreateFakeRoomRain(Room rm)
    {
        var rr = new RoomRain(rm.game.globalRain, rm);

        if (rm.waterObject != null && !ModManager.MSC && (rm.roomSettings.DangerType != RoomRain.DangerType.Flood || rm.roomSettings.DangerType != RoomRain.DangerType.FloodAndRain))
        {
            rm.waterObject.fWaterLevel = rm.waterObject.originalWaterLevel;
        }
        return rr;
    }

    private static void RegionState_AdaptRegionStateToWorld(ILContext il)
    {
        var c = new ILCursor(il);
        if (c.TryGotoNext(MoveType.After,
            x => x.MatchLdfld<SaveState>(nameof(SaveState.denPosition)),
            x => x.MatchCallvirt<World>("GetAbstractRoom")
            ))
        {
            c.Emit(OpCodes.Ldarg_0);
            c.EmitDelegate((AbstractRoom orig, RegionState self) =>
            {
                try
                {
                    if (orig != null) return orig;

                    else if (self.world.shelters.Length > 0)
                    {
                        orig = self.world.GetAbstractRoom(self.world.shelters[0]);
                        RWCustom.Custom.LogWarning($"Player's shelter is in a different region, so spawning in first shelter [{orig.name}]");
                    }
                    else
                    {
                        orig = self.world.GetAbstractRoom(self.world.firstRoomIndex);
                        RWCustom.Custom.LogWarning($"Player's shelter is in a different region and no shelters in region, so spawning in first room [{orig.name}]");
                    }
                    return orig;
                }
                catch(Exception e) { UnityEngine.Debug.LogError("Mergefix failed to prevent an exception from KeyItemTracking\n" + e); return orig; }
            });
        }
        else { MergeFixPlugin.BepLog("failed to il hook RegionState.AdaptRegionStateToWorld"); }
    }

    private static void RoomCamera_MoveCamera_Room_int(On.RoomCamera.orig_MoveCamera_Room_int orig, RoomCamera self, Room newRoom, int camPos)
    {
        orig(self, newRoom, camPos);
        if (!System.IO.File.Exists(WorldLoader.FindRoomFile(newRoom.abstractRoom.name, false, "_" + (camPos + 1).ToString() + "_bkg.png")))
        { self.preLoadedBKG = null; }
    }

    private static void RoomCamera_MoveCamera2(On.RoomCamera.orig_MoveCamera2 orig, RoomCamera self, string roomName, int camPos)
    {
        orig(self, roomName, camPos);
        if (!System.IO.File.Exists(WorldLoader.FindRoomFile(roomName, false, "_" + (camPos + 1).ToString() + "_bkg.png")))
        { self.preLoadedBKG = null; }
    }
}
