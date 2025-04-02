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
#warning check for watcher fix
        //IL.RegionState.AdaptWorldToRegionState += RegionState_AdaptRegionStateToWorld; check later, this code is pretty significantly alterred
        On.ShortcutGraphics.GenerateSprites += ShortcutGraphics_GenerateSprites;
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

    /// <summary>
    /// passaging with tracked items can cause a crash due to the item's saved location being in a different region from where it's spawning
    /// this fixes the bug (which only became a bug in the 1.9.15b update)
    /// </summary>
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
}
