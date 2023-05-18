**MergeFix** is a mod for Rain World that makes mod reloading exponentially faster and fixes some bugs related to mod merging.

To install, subscribe to the item on Steam Workshop.  
For a more technical explanation of what this mod does, keep reading.  

## Why does mod loading take so long?
The game generates a [checksum](https://en.wikipedia.org/wiki/Checksum) for every mod.  
This checksum represents the unique state of every file in the mod, so that if even the smallest value is changed, the game can see that the checksum is different and know to redo mod merging.  
Generating this checksum however, involves reading through every byte of every file in every mod, whether it's installed or not. This is **really slow.**
### So what does MergeFix do about it?
MergeFix uses each file's size and last modified metadata to generate the checksum, which is significantly faster than reading every byte in the file, and will have virtually the same result. MergeFix also only generates the checksum for enabled mods, because disabled mod checksums are never read.


## Why do my mods shuffle each time I apply them?
Internally, the game has a variable called InstalledMods, which is a list of every installed mod. It is ordered first by all the mods in StreamingAssets\mods\ sorted alphabetically, and then all workshop mods, sorted alphabetically.  
When the Remix menu wants to assign the new load order to these mods, it creates a new list that is the same length of InstalledMods, and then assigns each index of this list to be its position in the Remix menu. The highest mod will be assigned 0, then next highest will be 1, next will be 2, ect.  
But this order is reverse what it should be - the highest mod should be assigned the highest value in the actual load order. So the Remix menu flips its list backwards, so that the values line up right... except it flips them wrong.

Here's a comparison of how the list transform is intended versus what the actual result is.

| mod  | menu order | intended | actual |
| - |:-:|:-:|:-:|
| dev | 3 | 1 | 0 |
| exp | 2 | 2 | 1 |
| jolly | 4 | 0 | 4 |
| msc | 1 | 3 | 2 |
| mmf | 0 | 4 | 3 |

So the values are flipped based on alphabetical order instead of load order.

### So what does MergeFix do about it?

After this list is created, MergeFix flips it back, then subtracts each value from the highest value present. In the example above, dev's load order becomes 4 - 3 = 1, which matches the intended value.


## Why is my mod's room not replacing MSC's?
The mergedmods directory is for files generated through [modification files.](https://rainworldmodding.miraheze.org/wiki/Downpour_Reference/Modification_Files) But when generating mergedmods, instead of only reading through each mod's modify folder, the game reads through every file in every mod. It does this because to modify something, it first needs to find a file to modify, which won't always be a vanilla file. However, when picking the base file, it accidentally has priority order backwards, and will grab the file that's lowest in the load order. The base file is also put in mergedmods even if there are no modification files for it, so it'll override any other files.

### So what does MergeFix do about it?

By only reading the modify folder, and then using AssetManager.ResolveFilePath (the game's built-in function to find the highest priority version of a file) to pick the base file only when a modification file is present, all load order quirks are avoided. This also has the added benefit of making mod merging faster, as it doesn't have to read every file of every mod, only the files that are being modified.
