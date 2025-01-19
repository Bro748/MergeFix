**MergeFix** is a mod for Rain World that makes mod reloading significantly faster and fixes many bugs.

To install, subscribe to the item on Steam Workshop.  
For a more technical explanation of what this mod does, keep reading.  

## Why does mod loading take so long?
The game generates a [checksum](https://en.wikipedia.org/wiki/Checksum) for every mod.  
This checksum represents the unique state of every file in the mod, so that if even the smallest value is changed, the game can see that the checksum is different and know to redo mod merging.  
Generating this checksum however, involves reading through every byte of every file in every mod, whether it's installed or not. This is **really slow.**
### So what does MergeFix do about it?
MergeFix uses each file's size and last modified metadata to generate the checksum, which is significantly faster than reading every byte in the file, and will have virtually the same result. MergeFix also only generates the checksum for enabled mods, because disabled mod checksums are never read.