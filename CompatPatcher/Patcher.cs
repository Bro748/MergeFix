using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using PropertyAttributes = Mono.Cecil.PropertyAttributes;

namespace CompatPatcher;

public class Patcher
{
    private static readonly ManualLogSource Logger = BepInEx.Logging.Logger.CreateLogSource("CompatPatcher");
    public static IEnumerable<string> TargetDLLs => ["Assembly-CSharp.dll"];

    public static void Patch(AssemblyDefinition assembly)
    {
        try
        {
            var voidType = assembly.MainModule.ImportReference(typeof(void));
            var logError = assembly.MainModule.ImportReference(typeof(Patcher).GetMethod(nameof(LogStack), BindingFlags.Public | BindingFlags.Static));

            var tileType = assembly.MainModule.GetType("Room").NestedTypes.First(x => x.Name == "Tile");
            var terrainType = tileType.NestedTypes.First(x => x.Name == "TerrainType");
            var terrainField = tileType.Fields.FirstOrDefault(x => x.Name == "Terrain");

            var terrainProp = new PropertyDefinition("Terrain", PropertyAttributes.None, terrainType)
            {
                GetMethod = new MethodDefinition("get_Terrain", MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName, terrainType),
                SetMethod = new MethodDefinition("set_Terrain", MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName, voidType)
            };

            var proc = terrainProp.GetMethod.Body.GetILProcessor();

            proc.Append(Instruction.Create(OpCodes.Ldstr, "Call to obsolete property Room.Tile.Terrain's getter"));
            proc.Append(Instruction.Create(OpCodes.Call, logError));

            proc.Append(Instruction.Create(OpCodes.Ldarg_0));
            proc.Append(Instruction.Create(OpCodes.Ldfld, terrainField));
            proc.Append(Instruction.Create(OpCodes.Ret));

            proc = terrainProp.GetMethod.Body.GetILProcessor();

            proc.Append(Instruction.Create(OpCodes.Ldstr, "Call to obsolete property Room.Tile.Terrain's setter"));
            proc.Append(Instruction.Create(OpCodes.Call, logError));

            proc.Append(Instruction.Create(OpCodes.Ldarg_0));
            proc.Append(Instruction.Create(OpCodes.Stfld, terrainField));
            
            tileType.Properties.Add(terrainProp);
            tileType.Methods.Add(terrainProp.GetMethod);
            tileType.Methods.Add(terrainProp.SetMethod);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex);
        }
    }

    public static void LogStack(string text)
    {
        UnityEngine.Debug.LogError(text + Environment.NewLine + UnityEngine.StackTraceUtility.ExtractStackTrace());
    }
}