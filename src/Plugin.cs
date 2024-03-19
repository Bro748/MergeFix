﻿using BepInEx;
using System.Collections.Generic;
using System.IO;
using System.Security.Permissions;
using System.Text.RegularExpressions;
using RWCustom;
using System.Linq;
using System;
using BepInEx.Logging;
using System.Security.Cryptography;
using System.Text;
using MonoMod.Cil;
using Mono.Cecil.Cil;
using UnityEngine;
using IL.MoreSlugcats;

// Allows access to private members
#pragma warning disable CS0618
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
#pragma warning restore CS0618

namespace FixedMerging;

[BepInPlugin("bro.fixedmerging", "Fixed Merging", "1.2.0")]
sealed class Plugin : BaseUnityPlugin
{
    public void OnEnable()
    {
        // Add hooks here
        try
        {
            On.ModManager.ModMerger.ExecutePendingMerge += ModMerger_ExecutePendingMerge;
            On.ModManager.GenerateMergedMods += ModManager_GenerateMergedMods;
            //On.AssetManager.CreateDirectoryMd5 += AssetManager_CreateDirectoryMd5;
            On.ModManager.ModMerger.DeterminePaletteConflicts += ModMerger_DeterminePaletteConflicts;
            On.ModManager.ModMerger.UpdatePaletteLineWithConflict += ModMerger_UpdatePaletteLineWithConflict;
            On.ModManager.ModApplyer.ApplyModsThread += ModApplyer_ApplyModsThread;
            //IL.ModManager.ModMerger.PendingApply.ApplyMerges += PendingApply_ApplyMerges;
            //IL.ModManager.LoadModFromJson += ModManager_LoadModFromJson1;
            //IL.Menu.ModdingMenu.Singal += ModdingMenu_Singal;
            IL.PNGSaver.SaveTextureToFile += PNGSaver_SaveTextureToFile;
            IL.AssetManager.ResolveFilePath_string_bool += AssetManager_ResolveFilePath;
            IL.AssetManager.ResolveDirectory += AssetManager_ResolveDirectory;
        }
        catch (Exception e) { Logger.LogError(e); }
    }

    private void AssetManager_ResolveDirectory(ILContext il)
    {
        ReverseForLoop(il, 3);
    }

    private void AssetManager_ResolveFilePath(ILContext il)
    {
        ReverseForLoop(il, 1);
    }

    private void ReverseForLoop(ILContext il, int loc)
    {
        var c = new ILCursor(il);
        if (c.TryGotoNext(MoveType.Before,
            x => x.MatchLdcI4(0),
            x => x.MatchStloc(loc),
            x => x.MatchBr(out _),
            x => x.MatchLdsfld<ModManager>(nameof(ModManager.ActiveMods))
            ))
        {
            c.Index++;
            c.Emit(OpCodes.Pop);
            c.EmitDelegate(() => ModManager.ActiveMods.Count - 1);
        }
        else { Logger.LogError("flip AssetManager pt 1 failed"); return; }

        if (c.TryGotoNext(MoveType.After,
            x => x.MatchLdloc(loc),
            x => x.MatchLdcI4(1),
            x => x.MatchAdd()
            ))
        {
            c.EmitDelegate((int i) => i - 2);
        }
        else { Logger.LogError("flip AssetManager pt 2 failed"); return; }

        ILLabel label = null!;
        if (c.TryGotoNext(MoveType.After,
            x => x.MatchLdloc(loc),
            x => x.MatchLdsfld<ModManager>(nameof(ModManager.ActiveMods)),
            x => x.MatchCallvirt(out _),
            x => x.MatchBlt(out label)
            ))
        {
            c.Index -= 3;
            c.RemoveRange(3);
            c.Emit(OpCodes.Ldc_I4_0);
            c.Emit(OpCodes.Bge, label);
        }
        else { Logger.LogError("flip AssetManager pt 3 failed"); return; }
    }

    /// <summary>
    /// I told vigaro to do this for map merger code, as otherwise it eats up a ton of memory
    /// </summary>
    private void PNGSaver_SaveTextureToFile(ILContext il)
    {
        var c = new ILCursor(il);
        if (c.TryGotoNext(MoveType.After, x => x.MatchCall(typeof(File), nameof(File.WriteAllBytes))))
        {
            c.Emit(OpCodes.Ldloc_0);
            c.Emit(OpCodes.Call, typeof(UnityEngine.Object).GetMethod(nameof(Destroy), new Type[] {typeof(UnityEngine.Object)}));
        }
        else
        {
            Logger.LogError("failed to il hook PNGSaver!");
        }
    }

    /// <summary>
    /// fix checksums not updating on applying causing double-applying
    /// </summary>
    private void ModApplyer_ApplyModsThread(On.ModManager.ModApplyer.orig_ApplyModsThread orig, ModManager.ModApplyer self)
    {
        orig(self);
        for (int i = 0; i < ModManager.InstalledMods.Count; i++)
        { self.manager.rainWorld.options.modChecksums[ModManager.InstalledMods[i].id] = ModManager.InstalledMods[i].checksum; }
    }

    private string ModMerger_UpdatePaletteLineWithConflict(On.ModManager.ModMerger.orig_UpdatePaletteLineWithConflict orig, ModManager.ModMerger self, string lineKey, string lineValue)
    {
        return lineKey + ": " + lineValue;
    }
    /// <summary>
    /// if you think hard enough, this doesn't actually do anything.
    /// finding two palette60, rename one to palette160, and then update all room settings to use palette160;
    /// that's exactly the same as just using the highest palette60 in the load order
    /// </summary>
    private void ModMerger_DeterminePaletteConflicts(On.ModManager.ModMerger.orig_DeterminePaletteConflicts orig, ModManager.ModMerger self, string modPath)
    {
        return; //no
    }

    /// <summary>
    /// FIX LOAD ORDER SCRAMBLE (this is in base game now, not applied)
    /// </summary>
    private void ModdingMenu_Singal(ILContext il)
    {
        var c = new ILCursor(il);
        if (c.TryGotoNext(MoveType.After,
            x => x.MatchLdloc(3),
            x => x.MatchCall(typeof(Enumerable), nameof(Enumerable.Reverse)),
            x => x.MatchCall(typeof(Enumerable), nameof(Enumerable.ToList))
            ))
        {
            c.EmitDelegate((List<int> list) => {
                list.Reverse(); //flip it back so that the values line up with the mods
                int highest = list.Max((int t) => t); //find the highest mod
                list = list.Select(t => highest - t).ToList(); //invert the real load order
                return list;
            });
        }

        else { Logger.LogError("failed to hook ALoadModFromJson!"); }
    }

    /// <summary>
    /// DON'T GENERATE CHECKSUMS FOR DISABLED MODS (that's silly)
    /// BUT ENABLEDMODS ISN'T ALWAYS UP TO DATE!!! so we can't do this now
    /// base game does this
    /// </summary>
    private void ModManager_LoadModFromJson1(ILContext il)
    {
        var c = new ILCursor(il);
        if (c.TryGotoNext(MoveType.After,
            x => x.MatchLdloc(0),
            x => x.MatchLdfld<ModManager.Mod>("checksumOverrideVersion")
            ))
        {
            c.Emit(OpCodes.Ldarg_0);
            c.Emit(OpCodes.Ldloc_0);
            c.EmitDelegate((bool flag, RainWorld rainWorld, ModManager.Mod mod) => flag || !rainWorld.options.enabledMods.Contains(mod.id));
        }

        else { Logger.LogError("failed to hook ALoadModFromJson!"); }
    }

    private void PendingApply_ApplyMerges(MonoMod.Cil.ILContext il)
    {
        var c = new ILCursor(il);
        if (c.TryGotoNext(MoveType.After,
            x => x.MatchLdloc(0),
            x => x.MatchLdstr("_settings.txt"),
            x => x.MatchCallvirt<string>("EndsWith"),
            x => x.MatchBrtrue(out _),
            x => x.MatchLdloc(0),
            x => x.MatchLdstr("_settingstemplate_"),
            x => x.MatchCallvirt<string>("Contains")
            ))
        {
            c.Emit(OpCodes.Ldloc_0);
            c.EmitDelegate((bool flag, string fileName) => flag || fileName.Contains("settings-"));
        }

        else { Logger.LogError("failed to hook ApplyMerges!"); }
    }


    /// <summary>
    /// MASSIVE SPEEDBOOST
    /// </summary>
    /*private string AssetManager_CreateDirectoryMd5(On.AssetManager.orig_CreateDirectoryMd5 orig, string srcPath, string relativeRoot, List<string> skipFilenames)
    {
        string[] array = (from p in Directory.GetFiles(srcPath, "*.txt", SearchOption.AllDirectories)
                          orderby p
                          select p).ToArray<string>();
        string result;
        using (MD5 md = MD5.Create())
        {
            foreach (string text in array)
            {
                string s = text;
                if (relativeRoot != "" && text.Contains(relativeRoot))
                {
                    s = text.Substring(text.IndexOf(relativeRoot) + relativeRoot.Length);
                }
                byte[] bytes = Encoding.UTF8.GetBytes(s);
                md.TransformBlock(bytes, 0, bytes.Length, bytes, 0);
                //byte[] array3 = File.ReadAllBytes(text); //<<<<<REMOVED
                FileInfo pho = new(text);
                byte[] bytes2 = Encoding.UTF8.GetBytes(pho.Length.ToString()); //<<<<<ADDED (exponentially faster)
                md.TransformBlock(bytes2, 0, bytes2.Length, bytes2, 0);
            }
            md.TransformFinalBlock(new byte[0], 0, 0);
            result = BitConverter.ToString(md.Hash).Replace("-", "").ToLower();
        }
        return result;
    }*/

    /// <summary>
    /// PT 1 OF "DON'T MERGE MORE THAN YOU NEED"
    /// </summary>
    private void ModManager_GenerateMergedMods(On.ModManager.orig_GenerateMergedMods orig, ModManager.ModApplyer applyer, List<bool> pendingEnabled, bool hasRegionMods)
    {
        try
        {
            string path = Path.Combine(Custom.RootFolderDirectory(), "mergedmods");
            if (Directory.Exists(path))
            {
                string[] directories = Directory.GetDirectories(path);
                for (int i = 0; i < directories.Length; i++)
                {
                    if (hasRegionMods && Path.GetFileName(directories[i]).ToLowerInvariant() == "world")
                    {
                        string[] directories2 = Directory.GetDirectories(directories[i]);
                        for (int j = 0; j < directories2.Length; j++)
                        {
                            if (!(Path.GetFileName(directories2[j]).ToLowerInvariant() == "indexmaps"))
                            {
                                Directory.Delete(directories2[j], true);
                            }
                        }
                        string[] files = Directory.GetFiles(directories[i]);
                        for (int k = 0; k < files.Length; k++)
                        {
                            File.Delete(files[k]);
                        }
                    }
                    else
                    {
                        Directory.Delete(directories[i], true);
                    }
                }
                string[] files2 = Directory.GetFiles(path);
                for (int l = 0; l < files2.Length; l++)
                {
                    File.Delete(files2[l]);
                }
            }
            else
            {
                Directory.CreateDirectory(path);
            }
            ModManager.ModMerger modMerger = new();
            List<ModManager.Mod> mods = ModManager.InstalledMods.OrderBy(o => o.loadOrder).ToList();

            foreach (ModManager.Mod mod in mods)
            {
                int id = -1;
                for (int n = 0; n < ModManager.InstalledMods.Count; n++)
                {
                    if (ModManager.InstalledMods[n].id == mod.id)
                    {
                        id = n;
                        break;
                    }
                }
                if (id != -1 && pendingEnabled[id])
                {
                    applyer.activeMergingMod = id;
                    modMerger.DeterminePaletteConflicts(mod.path);
                    string modifyPath = Path.Combine(mod.path, "modify");
                    if (!Directory.Exists(modifyPath)) continue;

                    string[] modFiles = Directory.GetFiles(Path.Combine(mod.path, "modify"), "*.txt", SearchOption.AllDirectories);
                    applyer.mergeFileInd = 0;
                    applyer.mergeFileLength = modFiles.Length;
                    for (int num2 = 0; num2 < modFiles.Length; num2++)
                    {
                        applyer.mergeFileInd = num2;
                        string text = modFiles[num2];
                        string text2 = text.Substring(text.IndexOf(mod.path) + mod.path.Length).ToLowerInvariant();
                        text2 = text2.Substring(text2.Substring(1).IndexOf(Path.DirectorySeparatorChar) + 1);

                        modMerger.AddPendingApply(mod, text2, text, false, true); //only modification files are added to this list now
                    }
                }
            }
            modMerger.ExecutePendingMerge(applyer);
            modMerger.MergeWorldMaps(applyer);
        }
        catch (Exception e) { Logger.LogError(e); throw; }
    }

    /// <summary>
    /// PT 2 OF "DON'T MERGE MORE THAN YOU NEED" (and some other priority fixes)
    /// </summary>
    private void ModMerger_ExecutePendingMerge(On.ModManager.ModMerger.orig_ExecutePendingMerge orig, ModManager.ModMerger self, ModManager.ModApplyer applyer)
    {
        try
        {
            applyer.applyFileInd = 0;
            applyer.applyFileLength = self.moddedFiles.Count;

            foreach (KeyValuePair<string, List<ModManager.ModMerger.PendingApply>> kvp in self.moddedFiles)
            {
                applyer.applyFileInd++;
                string fileName = kvp.Key;
                List<ModManager.ModMerger.PendingApply> pendingApplies = kvp.Value;

                //don't merge if is strings.txt (this is excluded in ModMerger.WriteMergedFile so there's no point in bothering)
                if (pendingApplies.Where(x => x.filePath.Contains("strings.txt")).Count() > 0) continue;

                //find original file
                string originPath = AssetManager.ResolveFilePath(fileName.Substring(1));
                if (!File.Exists(originPath))
                { continue; Logger.LogInfo($"File does not exist {originPath}"); originPath = ""; } //either skip or make a blank

                //create base merge file
                string mergedPath = (Custom.RootFolderDirectory() + Path.DirectorySeparatorChar.ToString() + "mergedmods" + fileName).ToLowerInvariant();
                Directory.CreateDirectory(Path.GetDirectoryName(mergedPath));

                if (originPath == "")
                { File.WriteAllText(mergedPath, ""); } //yes, we might want files that don't have origins

                else { File.Copy(originPath, mergedPath, true); }

                //apply merges
                List<ModManager.ModMerger.PendingApply> merges = new();
                List<ModManager.ModMerger.PendingApply> modifications = new();

                for (int j = 0; j < pendingApplies.Count; j++)
                {
                    if (pendingApplies[j].mergeLines != null)
                    { merges.Add(pendingApplies[j]); }

                    if (pendingApplies[j].isModification)
                    { modifications.Add(pendingApplies[j]); }
                }

                //merges always go first
                foreach (ModManager.ModMerger.PendingApply pendingApply6 in merges)
                {
                    pendingApply6.ApplyMerges(pendingApply6.modApplyFrom, self, mergedPath);
                }
                foreach (ModManager.ModMerger.PendingApply pendingApply7 in modifications)
                {
                    pendingApply7.ApplyModifications(mergedPath);
                }
            }
            applyer.applyFileLength = 0;
            applyer.applyFileInd = 0;
        }
        catch (Exception e) { Logger.LogError(e); throw; }
    }
}
