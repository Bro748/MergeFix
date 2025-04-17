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
        On.Room.Unloaded += Room_Unloaded;
        IL.OverWorld.WorldLoaded += OverWorld_WorldLoaded;
    }

    private static void OverWorld_WorldLoaded(ILContext il)
    {
        var c = new ILCursor(il);

        while (c.TryGotoNext(MoveType.After,
            x => x.MatchLdloc(0),
            x => x.MatchLdfld<World>("name"),
            x => x.MatchLdloc(1),
            x => x.MatchLdfld<World>("name"),
            x => x.MatchCall<string>("op_Inequality")
            ))
        { }

        c.Emit(OpCodes.Ldloc, 0);
        c.Emit(OpCodes.Ldloc, 1);
        c.EmitDelegate((bool orig, World oldWorld, World newWorld) => {
            if (oldWorld.name != newWorld.name)
            {
                foreach (var room in oldWorld.activeRooms)
                {
                    if (room.world == oldWorld)
                        room.Unloaded();
                }
            }

            return false;
        });
    }

    //not yet implemented in 1.10.0 because buggy
    private static void Room_Unloaded(On.Room.orig_Unloaded orig, Room self)
    {
        Futile.atlasManager.UnloadAtlas("RainMask_" + self.abstractRoom.name);
        orig(self);
    }
}
