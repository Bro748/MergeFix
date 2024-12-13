using BepInEx;
using System.Collections.Generic;
using RWCustom;
using MonoMod.Cil;
using Mono.Cecil.Cil;
using UnityEngine;
using System;
using static ModManager.MapMerger;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace MergeFix;

internal static class MapMergerFixesOop
{
    public static void ApplyHooks()
    {
        On.ModManager.MapMerger.GraftOnto += MapMerger_GraftOnto;
        On.ModManager.MapMerger.MapConnectionLine.SoftEquals += MapConnectionLine_SoftEquals;
        IL.ModManager.MapMerger.MergeMapFiles += MapMerger_MergeMapFiles;
        On.ModManager.MapMerger.MergeMapData.GenerateDefault += MergeMapData_GenerateDefault;
        IL.ModManager.MapMerger.MergeWorldMaps += MapMerger_MergeWorldMaps;
    }

    private static void MapMerger_MergeWorldMaps(ILContext il)
    {
        try
        {
            var c = new ILCursor(il);

            c.GotoNext(MoveType.Before,
                x => x.MatchLdloc(0),
                x => x.MatchCallvirt(typeof(List<string>).GetProperty(nameof(List<string>.Count)).GetGetMethod()),
                x => x.MatchBrtrue(out _));
            ILLabel brTo = c.MarkLabel();

            c.Index = 0;
            c.GotoNext(MoveType.After,
                x => x.MatchNewobj<List<string>>(),
                x => x.MatchStloc(0));

            c.Emit(OpCodes.Ldarg_0);
            c.Emit(OpCodes.Ldarg_1);
            c.Emit(OpCodes.Ldloc_0);

            c.EmitDelegate(delegate (ModManager.ModMerger merger, ModManager.ModApplyer applyer, List<string> list)
            {
                foreach (MergeMapData mergeMapData in merger.modMapData)
                {
                    string path = AssetManager.ResolveFilePath(Path.Combine("world", mergeMapData.region, "map_" + mergeMapData.MapKey + ".png"));
                    if (File.Exists(path))
                    {
                        string mapKey = mergeMapData.MapKey;
                        if (!applyer.worldMaps.ContainsKey(mapKey))
                        {
                            applyer.worldMaps[mapKey] = [];
                        }
                        applyer.worldMaps[mapKey].Add(mergeMapData);
                        if (!list.Contains(mapKey))
                        {
                            list.Add(mapKey);
                        }
                        applyer.worldMapImages[mapKey] = [path];
                    }
                }
            });
            c.Emit(OpCodes.Br_S, brTo);
        }
        catch (Exception e) { MergeFixPlugin.BepLog("failed to il hook MapMerger.MergeWorldMaps\n" + e); }
    }

    /// <summary>
    /// too lazy to il hook
    /// </summary>
    private static MergeMapData MergeMapData_GenerateDefault(On.ModManager.MapMerger.MergeMapData.orig_GenerateDefault orig, string sourcePath, ModManager.Mod modApplyFrom, bool baseFile)
    {
        string text = "";
        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(sourcePath);
        string text2 = fileNameWithoutExtension.Replace("map_", "");
        if (text2.Contains('-'))
        {
            string[] array = text2.Split(new char[]
            {
            '-'
            });
            if (array.Length == 2)
            {
                text = array[1];
                text2 = array[0];
            }
        }
        string text3 = ResolveVersionSource(modApplyFrom, Path.Combine("world", text2, fileNameWithoutExtension + ".txt"), true);
        return new MergeMapData(text2, text, Path.GetFileNameWithoutExtension(sourcePath), text3, default, baseFile ? null : modApplyFrom);
    }

    private static void MapMerger_MergeMapFiles(ILContext il)
    {
        var c = new ILCursor(il);
        if (c.TryGotoNext(MoveType.After, x => x.MatchCall(typeof(ModManager.MapMerger), "TryFindRoomOffset")))
        {
            c.Emit(OpCodes.Ldloc, 24);
            c.Emit(OpCodes.Ldloc, 15);
            c.EmitDelegate((bool orig, MapRoomLine room, List<string> list) => {
                if (orig && room.roomSize == default) list.Add(room.roomName);
                return orig;
            });
        }
        while (c.TryGotoNext(MoveType.Before, x => x.MatchCall(typeof(Math),"Ceiling")))
        {
            c.Remove();
            c.EmitDelegate((double d) => Math.Floor(d));
        }
    }

    private static bool MapConnectionLine_SoftEquals(On.ModManager.MapMerger.MapConnectionLine.orig_SoftEquals orig, ref MapConnectionLine self, MapConnectionLine line)
    {
        //oop
        return (self.roomNameA == line.roomNameB && self.roomNameB == line.roomNameA) || (self.roomNameA == line.roomNameA && self.roomNameB == line.roomNameB);
    }

    private static void MapMerger_GraftOnto(On.ModManager.MapMerger.orig_GraftOnto orig, Texture2D mapTexture, Texture2D localTexture, ref Vector2 mapOrigin, Vector2 localBottomLeft)
    {
        var timer = Stopwatch.StartNew();

        //the png is all 3 layers stacked on top of each other, these variables simplify the following calculations
        int mapTextureSubHeight = mapTexture.height / 3;
        int localTextureSubHeight = localTexture.height / 3;

        //pre-calculating stuff used to determine where the maps are positioned relative to what's already there
        int largestWidth = Mathf.Max(localTexture.width + (int)localBottomLeft.x, mapTexture.width + (int)mapOrigin.x) - Mathf.Min((int)mapOrigin.x, (int)localBottomLeft.x);
        int largestHeight = Mathf.Max(localTextureSubHeight + (int)localBottomLeft.y, mapTextureSubHeight + (int)mapOrigin.y) - Mathf.Min((int)mapOrigin.y, (int)localBottomLeft.y);
        int xOrigin = (int)(localBottomLeft.x - mapOrigin.x);
        int yOrigin = (int)(localBottomLeft.y - mapOrigin.y);

        //if the new map goes beyond the bounds of the old map or vice versa,
        //resize accordingly so that they match size and position
        if (mapTexture.width < largestWidth || mapTextureSubHeight < largestHeight)
        {
            UnityEngine.Debug.Log("resizing map to " + new Vector2(largestWidth, largestHeight));
            OmniResize(mapTexture, Math.Max(0, 0 - xOrigin), Math.Max(0, 0 - yOrigin), largestWidth - mapTexture.width, largestHeight - mapTextureSubHeight);

            //set the map origin to what it was just resized to, so that future merges can be done properly
            mapOrigin = new Vector2(Math.Min(mapOrigin.x, localBottomLeft.x), Math.Min(mapOrigin.y, localBottomLeft.y));
        }

        UnityEngine.Debug.Log("merging at position " + (localBottomLeft - mapOrigin));
        TransBlit(mapTexture, localTexture, IntVector2.FromVector2(localBottomLeft - mapOrigin));


        //orig(mapTexture, localTexture, ref mapOrigin, localBottomLeft);
        UnityEngine.Debug.Log("map merge took: " + timer.ElapsedMilliseconds);

        
    }
    public static void TransBlit(Texture2D texture, Texture2D texture2, IntVector2 offset)
    {
        int mapTextureSubHeight = texture.height / 3;
        int localTextureSubHeight = texture2.height / 3;

        Color32 color = new Color32(0, byte.MaxValue, 0, byte.MaxValue);
        Color32[] pixels = texture.GetPixels32();
        Color32[] pixels2 = texture2.GetPixels32();

        for (int l = 0; l <= 2; l++)
        {
            for (int i = 0; i < texture2.width; i++)
            {
                for (int j = 0; j < localTextureSubHeight; j++)
                {
                    Color32 color2 = pixels2[i + (j + (localTextureSubHeight * l)) * texture2.width];
                    if (color2.r != color.r || color2.g != color.g || color2.b != color.b || color2.a != color.a)
                    {
                        if (color2.g == color.g / 2 && color2.r == 0 && color2.b == 0 && color2.a == color.a)
                        { color2 = color; }
                        pixels[(i + offset.x) + (j + offset.y + (mapTextureSubHeight * l)) * texture.width] = color2;
                    }
                }
            }
        }

        texture.SetPixels32(pixels);
    }
}
