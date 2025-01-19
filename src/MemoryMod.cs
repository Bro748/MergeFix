using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using System.Collections.Generic;
using System.IO;
using System;
using Mono.Cecil.Cil;
using UnityEngine;
using System.Reflection;

namespace MergeFix;

/// <summary>
/// this is mostly destroying various PNGs the game uses that it doesn't destroy
/// </summary>
internal static class MemoryMod
{
    public static void ApplyHooks()
    {
        IL.PNGSaver.SaveTextureToFile += PNGSaver_SaveTextureToFile;
        On.PlayerProgression.Revert += PlayerProgression_Revert;
        new Hook(typeof(HUD.Map).GetProperty(nameof(HUD.Map.discoverTexture), BindingFlags.Public | BindingFlags.Instance).GetSetMethod(), Map_SetDiscoverTexture);
        On.PlayerProgression.LoadByteStringIntoMapTexture += PlayerProgression_LoadByteStringIntoMapTexture;
        On.Room.Unloaded += Room_Unloaded;
        On.Menu.Remix.MenuModList.cctor += MenuModList_cctor;
    }

    private static void MenuModList_cctor(On.Menu.Remix.MenuModList.orig_cctor orig)
    {
        orig();

        //we have to hook weirdly late due to static constructor nonsense
        On.Menu.Remix.MenuModList.ModButton._ProcessThumbnail += ModButton__ProcessThumbnail;
        On.Menu.Remix.MenuModList.cctor += MenuModList_cctor;
    }

    private static void ModButton__ProcessThumbnail(On.Menu.Remix.MenuModList.ModButton.orig__ProcessThumbnail orig, Menu.Remix.MenuModList.ModButton self)
    {
        try
        {
            UnityEngine.Object.Destroy(self._thumb);
            UnityEngine.Object.Destroy(self._thumbG);
        }
        catch { }
        orig(self);
    }

    private static void Room_Unloaded(On.Room.orig_Unloaded orig, Room self)
    {
        Futile.atlasManager.UnloadAtlas("RainMask_" + self.abstractRoom.name);
        orig(self);
    }


    /// <summary>
    /// I told vigaro to do this for map merger code, as otherwise it eats up a ton of memory
    /// </summary>
    private static void PNGSaver_SaveTextureToFile(ILContext il)
    {
        var c = new ILCursor(il);
        if (c.TryGotoNext(MoveType.After, x => x.MatchCall(typeof(File), nameof(File.WriteAllBytes))))
        {
            c.Emit(OpCodes.Ldloc_0);
            c.Emit(OpCodes.Call, typeof(UnityEngine.Object).GetMethod(nameof(UnityEngine.Object.Destroy), [typeof(UnityEngine.Object)]));
        }
        else
        {
            MergeFixPlugin.BepLog("failed to il hook PNGSaver!");
        }
    }


    public static void DestroyMapTexture(Dictionary<string, Texture2D> dict, string regionName, Texture2D oldTexture = null)
    {
        if (dict.ContainsKey(regionName) && dict[regionName] != oldTexture)
        { UnityEngine.Object.Destroy(dict[regionName]); }
    }

    private static void PlayerProgression_LoadByteStringIntoMapTexture(On.PlayerProgression.orig_LoadByteStringIntoMapTexture orig, PlayerProgression self, string regionName, string byteString)
    {
        DestroyMapTexture(self.mapDiscoveryTextures, regionName);
        orig(self, regionName, byteString);
    }

    public static void Map_SetDiscoverTexture(Action<HUD.Map, Texture2D> orig, HUD.Map self, Texture2D value)
    {
        DestroyMapTexture(self.hud.rainWorld.progression.mapDiscoveryTextures, self.mapData.regionName, value);
        orig(self, value);
    }

    private static void PlayerProgression_Revert(On.PlayerProgression.orig_Revert orig, PlayerProgression self)
    {
        foreach (Texture2D texture in self.mapDiscoveryTextures.Values)
        { UnityEngine.Object.Destroy(texture); }
        orig(self);
    }
}
