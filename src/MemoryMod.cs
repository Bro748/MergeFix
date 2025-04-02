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
        //On.Room.Unloaded += Room_Unloaded;
    }

    //not yet implemented in 1.10.0 because buggy
    private static void Room_Unloaded(On.Room.orig_Unloaded orig, Room self)
    {
        Futile.atlasManager.UnloadAtlas("RainMask_" + self.abstractRoom.name);
        orig(self);
    }
}
