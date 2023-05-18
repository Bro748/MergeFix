using BepInEx;
using System.Collections.Generic;
using System.IO;
using System.Security.Permissions;
using System.Text.RegularExpressions;
using RWCustom;
using static System.Net.Mime.MediaTypeNames;
using System.Linq;
using System;
using BepInEx.Logging;
using System.Security.Cryptography;
using System.Text;

// Allows access to private members
#pragma warning disable CS0618
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
#pragma warning restore CS0618

namespace FixedMerging;

[BepInPlugin("bro.fixedmerging", "Fixed Merging", "0.1.0")]
sealed class Plugin : BaseUnityPlugin
{
    public void OnEnable()
    {
        // Add hooks here
        try
        {
            On.ModManager.ModMerger.ExecutePendingMerge += ModMerger_ExecutePendingMerge;
            On.ModManager.GenerateMergedMods += ModManager_GenerateMergedMods;
            On.ModManager.ModApplyer.ctor += ModApplyer_ctor;
            On.AssetManager.CreateDirectoryMd5 += AssetManager_CreateDirectoryMd5;
            On.ModManager.LoadModFromJson += ModManager_LoadModFromJson;
        }
        catch (Exception e) { Logger.LogError(e); }
    }

    /// <summary>
    /// DON'T GENERATE CHECKSUMS FOR DISABLED MODS (that's silly)
    /// </summary>
    private ModManager.Mod ModManager_LoadModFromJson(On.ModManager.orig_LoadModFromJson orig, RainWorld rainWorld, string modpath, string consolepath)
    {
        ModManager.Mod mod = new ModManager.Mod
        {
            id = Path.GetFileName(modpath),
            name = Path.GetFileName(modpath),
            version = "",
            hideVersion = false,
            targetGameVersion = "v1.9.07b",
            authors = "Unknown",
            description = "No Description.",
            path = modpath,
            consolePath = consolepath,
            checksum = "",
            checksumChanged = false,
            checksumOverrideVersion = false,
            requirements = new string[0],
            requirementsNames = new string[0],
            tags = new string[0],
            modifiesRegions = false,
            workshopId = 0UL,
            workshopMod = false,
            hasDLL = false,
            loadOrder = 0,
            enabled = false
        };
        if (File.Exists(modpath + Path.DirectorySeparatorChar.ToString() + "modinfo.json"))
        {
            Dictionary<string, object> dictionary = File.ReadAllText(modpath + Path.DirectorySeparatorChar.ToString() + "modinfo.json").dictionaryFromJson();
            if (dictionary == null)
            {
                UnityEngine.Debug.Log("FAILED TO DESERIALIZE MOD FROM JSON! " + modpath);
                return null;
            }
            foreach (KeyValuePair<string, object> pair in dictionary)
            {
                switch (pair.Key)
                {
                    case "id":
                        mod.id = pair.Value.ToString();
                        break;

                    case "name":
                        mod.name = pair.Value.ToString();
                        break;

                    case "version":
                        mod.version = pair.Value.ToString();
                        break;

                    case "hide_version":
                        mod.hideVersion = (bool)pair.Value;
                        break;

                    case "target_game_version":
                        mod.targetGameVersion = pair.Value.ToString();
                        break;

                    case "authors":
                        mod.authors = pair.Value.ToString();
                        break;

                    case "description":
                        mod.description = pair.Value.ToString();
                        break;

                    case "youtube_trailer_id":
                        mod.trailerID = pair.Value.ToString();
                        break;

                    case "requirements":
                        mod.requirements = ((List<object>)pair.Value).ConvertAll<string>((object x) => x.ToString()).ToArray();
                        break;

                    case "requirement_names":
                        mod.requirementsNames = ((List<object>)pair.Value).ConvertAll<string>((object x) => x.ToString()).ToArray();
                        break;

                    case "tags":
                        mod.tags = ((List<object>)pair.Value).ConvertAll<string>((object x) => x.ToString()).ToArray();
                        break;

                    case "checksum_override_version":
                        mod.checksumOverrideVersion = (bool)pair.Value;
                        break;

                }
            }
        }
        if (Directory.Exists((modpath + Path.DirectorySeparatorChar.ToString() + "world").ToLowerInvariant()))
        {
            mod.modifiesRegions = true;
        }
        if (Directory.Exists(Path.Combine(modpath, "plugins")) || Directory.Exists(Path.Combine(modpath, "patchers")))
        {
            mod.hasDLL = true;
        }
        string text;
        if (mod.checksumOverrideVersion || !rainWorld.options.enabledMods.Contains(mod.id)) //<<<<<new condition
        {
            text = mod.version;
        }
        else
        {
            text = ModManager.ComputeModChecksum(mod.path);
        }
        if (rainWorld.options.modChecksums.ContainsKey(mod.id))
        {
            mod.checksumChanged = (text != rainWorld.options.modChecksums[mod.id]);
            if (mod.checksumChanged)
            {
                UnityEngine.Debug.Log(string.Concat(new string[]
                {
                "MOD CHECKSUM CHANGED FOR ",
                mod.name,
                ": Was ",
                rainWorld.options.modChecksums[mod.id],
                ", is now ",
                text
                }));
            }
        }
        else
        {
            UnityEngine.Debug.Log("MOD CHECKSUM DID NOT EXIST FOR " + mod.name + ", NEWLY INSTALLED?");
            mod.checksumChanged = true;
        }
        mod.checksum = text;
        return mod;
    }

    /// <summary>
    /// MASSIVE SPEEDBOOST
    /// </summary>
    private string AssetManager_CreateDirectoryMd5(On.AssetManager.orig_CreateDirectoryMd5 orig, string srcPath, string relativeRoot, List<string> skipFilenames)
    {
        string[] array = (from p in Directory.GetFiles(srcPath, "*", SearchOption.AllDirectories)
                          orderby p
                          select p).ToArray<string>();
        string result;
        using (MD5 md = MD5.Create())
        {
            foreach (string text in array)
            {
                if ((skipFilenames == null || !skipFilenames.Contains(Path.GetFileName(text))) && !text.EndsWith(".meta") && !text.EndsWith(".png") && !text.ToLowerInvariant().EndsWith(".dll"))
                {
                    string s = text;
                    if (relativeRoot != "" && text.Contains(relativeRoot))
                    {
                        s = text.Substring(text.IndexOf(relativeRoot) + relativeRoot.Length);
                    }
                    byte[] bytes = Encoding.UTF8.GetBytes(s);
                    md.TransformBlock(bytes, 0, bytes.Length, bytes, 0);
                    //byte[] array3 = File.ReadAllBytes(text); //<<<<<REMOVED
                    byte[] bytes2 = Encoding.UTF8.GetBytes(new FileInfo(text).Length.ToString()); //<<<<<ADDED (exponentially faster)
                    md.TransformBlock(bytes2, 0, bytes2.Length, bytes2, 0);
                }
            }
            md.TransformFinalBlock(new byte[0], 0, 0);
            result = BitConverter.ToString(md.Hash).Replace("-", "").ToLower();
        }
        return result;
    }

    /// <summary>
    /// FIX LOAD ORDER SCRAMBLE
    /// </summary>
    private void ModApplyer_ctor(On.ModManager.ModApplyer.orig_ctor orig, ModManager.ModApplyer self, ProcessManager manager, List<bool> pendingEnabled, List<int> pendingLoadOrder)
    {
        orig(self, manager, pendingEnabled, pendingLoadOrder);
        self.pendingLoadOrder.Reverse(); //flip it back so that the values line up with the mods
        int highest = self.pendingLoadOrder.Max((int t) => t); //find the highest mod
        self.pendingLoadOrder = self.pendingLoadOrder.Select(t => highest - t).ToList(); //invert the real load order
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
            ModManager.ModMerger modMerger = new ModManager.ModMerger();
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
                if (pendingEnabled[id])
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

                //don't merge if is strings.txt
                if (pendingApplies.Where(x => x.filePath.Contains("strings.txt")).Count() > 0) continue;

                //find original file
                string originPath = AssetManager.ResolveFilePath(fileName.Substring(1));
                if (!File.Exists(originPath))
                { Log($"File does not exist {originPath}"); originPath = ""; }

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

    void Log(string str)
    {
        Logger.LogInfo(str);
    }
}
