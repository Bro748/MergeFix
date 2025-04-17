using BepInEx;
using System.Collections.Generic;
using System.IO;
using System.Security.Permissions;
using RWCustom;
using System.Linq;
using System;
using System.Security.Cryptography;
using System.Text;
using static ModManager;
using static ModManager.ModMerger;

using MonoMod.Cil;
using Mono.Cecil.Cil;
using UnityEngine;
using MonoMod.RuntimeDetour;
using System.Reflection;

// Allows access to private members
#pragma warning disable CS0618
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
#pragma warning restore CS0618

namespace MergeFix;

[BepInPlugin("bro.fixedmerging", "Fixed Merging", "1.71")]
sealed class MergeFixPlugin : BaseUnityPlugin
{
    public static MergeFixPlugin instance;
    internal static void BepLog(string message) => instance.Logger.LogMessage(message);
    public void OnEnable()
    {
        instance = this;
        // Add hooks here
        try
        {
            On.ModManager.ModMerger.ExecutePendingMerge += ModMerger_ExecutePendingMerge;
            On.ModManager.GenerateMergedMods += ModManager_GenerateMergedMods;
            On.AssetManager.CreateDirectoryMd5_string_string += AssetManager_CreateDirectoryMd5;
            On.ModManager.ModMerger.DeterminePaletteConflicts += ModMerger_DeterminePaletteConflicts;
            On.ModManager.ModMerger.UpdatePaletteLineWithConflict += ModMerger_UpdatePaletteLineWithConflict;
            //IL.ModManager.ModMerger.PendingApply.ApplyMerges += PendingApply_ApplyMerges;

            OtherFixes.ApplyHooks();
            MemoryMod.ApplyHooks();
        }
        catch (Exception e) { Logger.LogError(e); }
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
    /// MASSIVE SPEEDBOOST
    /// </summary>
    private string AssetManager_CreateDirectoryMd5(On.AssetManager.orig_CreateDirectoryMd5_string_string orig, string srcPath, string relativeRoot)
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
    }

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
                if (id == -1 || !pendingEnabled[id]) continue;

                modMerger.DeterminePaletteConflicts(mod.path);

                List<string> basePaths = new();
                if (mod.hasTargetedVersionFolder) basePaths.Add(mod.TargetedPath);
                if (mod.hasNewestFolder) basePaths.Add(mod.NewestPath);
                basePaths.Add(mod.path);

                List<string> usedPaths = new();

                foreach (string basePath in basePaths)
                {
                    applyer.activeMergingMod = id;
                    string modifyPath = Path.Combine(basePath, "modify");
                    if (!Directory.Exists(modifyPath)) continue;

                    string[] modFiles = Directory.GetFiles(Path.Combine(basePath, "modify"), "*.txt", SearchOption.AllDirectories);
                    applyer.mergeFileInd = 0;
                    applyer.mergeFileLength = modFiles.Length;
                    for (int num2 = 0; num2 < modFiles.Length; num2++)
                    {
                        applyer.mergeFileInd = num2;
                        string text = modFiles[num2];
                        string text2 = text.Substring(text.IndexOf(basePath) + basePath.Length).ToLowerInvariant();
                        text2 = text2.Substring(text2.Substring(1).IndexOf(Path.DirectorySeparatorChar) + 1);

                        if (!usedPaths.Contains(text2))
                        {
                            modMerger.AddPendingApply(mod, text2, text, true); //only modification files are added to this list now
                            usedPaths.Add(text2);
                        }
                    }
                }
            }
            modMerger.ExecutePendingMerge(applyer);
            ModManager.MapMerger.MergeWorldMaps(modMerger, applyer);
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

            Dictionary<Mod, List<PendingApply>> subMergeActions2 = new Dictionary<Mod, List<PendingApply>>();
            Dictionary<Mod, List<PendingApply>> subModifyActions2 = new Dictionary<Mod, List<PendingApply>>();
            Dictionary<PendingApply, string> subBasePaths = new Dictionary<PendingApply, string>();
            string mergedModsFolder = Custom.RootFolderDirectory() + Path.DirectorySeparatorChar + "mergedmods";

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


                    if (pendingApplies[j].subApplies != null)
                    {
                        Mod mod = pendingApplies[j].modApplyFrom;
                        foreach (PendingApply subApply in pendingApplies[j].subApplies)
                        {
                            if (subApply.mergeLines != null)
                            {
                                if (!subMergeActions2.TryGetValue(mod, out _))
                                {
                                    subMergeActions2[mod] = new List<PendingApply>();
                                }
                                subMergeActions2[mod].Add(subApply);
                                subBasePaths[subApply] = kvp.Key;
                            }
                            if (subApply.isModification)
                            {
                                if (!subModifyActions2.TryGetValue(mod, out _))
                                {
                                    subModifyActions2[mod] = new List<PendingApply>();
                                }
                                subModifyActions2[mod].Add(subApply);
                                subBasePaths[subApply] = kvp.Key;
                            }
                        }
                    }
                }

                //merges always go first
                foreach (PendingApply pendingApply6 in merges)
                {
                    pendingApply6.ApplyMerges(pendingApply6.modApplyFrom, self, mergedPath);
                }
                foreach (PendingApply pendingApply7 in modifications)
                {
                    pendingApply7.ApplyModifications(mergedPath);
                }
            }


            foreach (KeyValuePair<Mod, List<PendingApply>> pair in subMergeActions2.OrderBy(x => x.Key.loadOrder))
            {
                foreach (PendingApply applyAction in pair.Value)
                {
                    string mergedDestination = (mergedModsFolder + subBasePaths[applyAction]).ToLowerInvariant();
                    foreach (string destination in applyAction.fileDestinations)
                    {
                        string basePath = subBasePaths[applyAction].Substring(1);
                        string destinationFilePath = RelativePathToAbsolute(destination, mergedDestination);
                        string originFilePath = AssetManager.ResolveFilePath(basePath);
                        if (!File.Exists(destinationFilePath))
                        {
                            File.Copy(originFilePath, destinationFilePath);
                            if (mergedDestination.Contains("map_") && !mergedDestination.Contains("map_image_") && mergedDestination.EndsWith(".txt"))
                            {
                                string pngPath = basePath.Replace(".txt", ".png");
                                string pngDestination = destinationFilePath.Replace(".txt", ".png").Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar); ;
                                self.mergeMapInstructions[pngDestination] = new MapMerger.MergeMapInstructions(self.mergeMapInstructions.Count(), pngPath, pngDestination);
                            }
                        }

                        applyAction.ApplyMerges(applyAction.modApplyFrom, self, destinationFilePath);
                    }
                }
            }

            foreach (KeyValuePair<Mod, List<PendingApply>> pair in subModifyActions2.OrderBy(x => x.Key.loadOrder))
            {
                foreach (PendingApply applyAction in pair.Value)
                {
                    string mergedDestination = (mergedModsFolder + subBasePaths[applyAction]).ToLowerInvariant();
                    foreach (string destination in applyAction.fileDestinations)
                    {
                        string basePath = subBasePaths[applyAction].Substring(1);
                        string destinationFilePath = RelativePathToAbsolute(destination, mergedDestination);
                        string originFilePath = AssetManager.ResolveFilePath(basePath);
                        if (!File.Exists(destinationFilePath))
                        {
                            File.Copy(originFilePath, destinationFilePath);
                            if (mergedDestination.Contains("map_") && !mergedDestination.Contains("map_image_") && mergedDestination.EndsWith(".txt"))
                            {
                                string pngPath = basePath.Replace(".txt", ".png");
                                string pngDestination = destinationFilePath.Replace(".txt", ".png").Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar); ;
                                self.mergeMapInstructions[pngDestination] = new MapMerger.MergeMapInstructions(self.mergeMapInstructions.Count(), pngPath, pngDestination);
                            }
                        }

                        applyAction.ApplyModifications(destinationFilePath);
                    }
                }
            }


            applyer.applyFileLength = 0;
            applyer.applyFileInd = 0;
        }
        catch (Exception e) { Logger.LogError(e); throw; }
    }
}
