using BepInEx;
using System.Collections.Generic;
using RWCustom;
using MonoMod.Cil;
using Mono.Cecil.Cil;
using UnityEngine;
using System;
using static ModManager.MapMerger;
using System.Diagnostics;

namespace MergeFix;

internal static class MapMergerFixesOop
{
    public static void ApplyHooks()
    {
        On.ModManager.MapMerger.GraftOnto += MapMerger_GraftOnto;
        On.ModManager.MapMerger.MapConnectionLine.SoftEquals += MapConnectionLine_SoftEquals;
        IL.ModManager.MapMerger.MergeMapFiles += MapMerger_MergeMapFiles;

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
            OmniResize(mapTexture, Math.Max(0, 0 - xOrigin), Math.Max(0, 0 - yOrigin), largestWidth - mapTexture.width, largestHeight - mapTextureSubHeight);

            //set the map origin to what it was just resized to, so that future merges can be done properly
            mapOrigin = new Vector2(Math.Min(mapOrigin.x, localBottomLeft.x), Math.Min(mapOrigin.y, localBottomLeft.y));
        }

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

        for (int l = 0; l < 2; l++)
        {
            for (int i = 0; i < texture2.width; i++)
            {
                for (int j = 0; j < localTextureSubHeight; j++)
                {
                    Color32 color2 = pixels2[i + (j + localTextureSubHeight * l) * texture2.width];
                    if (color2.r != color.r || color2.g != color.g || color2.b != color.b || color2.a != color.a)
                    {
                        pixels[(i + offset.x) + (j + offset.y + mapTextureSubHeight * l) * texture.width] = color2;
                    }
                }
            }
        }

        texture.SetPixels32(pixels);
    }
}
