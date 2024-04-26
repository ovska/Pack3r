# Pack3r

Create release-ready pk3 archives quickly. Pack3r uses NetRadiant `.map`-files, shaders, mapscripts and sound/speakerscripts for asset discovery and includes only files required to play the map in the pk3.

```
Description:
  Pack3r, tool to create release-ready pk3s from NetRadiant maps

Usage:
  Pack3r.Console [<map>] [options]

Arguments:
  <map>  .map file to create the pk3 from (NetRadiant format) []

Options:
  --pk3 <pk3>                                    Destination to write the pk3 to, defaults to etmain (ignored on dry runs) []
  -d, --dry-run                                  Print files that would be packed without creating the pk3 [default: False]
  --allowpartial, --loose                        Pack the pk3 even if some assets are missing  (ignored on dry runs) [default: False]
  --includesource, --src                         Include source (.map, editorimages etc.) in pk3 [default: False]
  --shaderlist, --sl                             Only consider shaders included in shaderlist.txt [default: False]
  --force, --overwrite                           Overwrites an existing output pk3 file if one exists [default: False]
  -v, --verbosity <Debug|Error|Fatal|Info|Warn>  Output log level, use without parameter to view all output [default: Info]
  --version                                      Show version information
  -?, -h, --help                                 Show help and usage information
```

Example:

```bash
.\Pack3r.Console.exe 'C:\Temp\ET\map\ET\etmain\maps\sungilarity.map' --pk3 'C:\Temp\test.pk3' --loose --overwrite -v
```

### Limitations
- Only NetRadiant `.map` files are supported, not GTK radiant
- `.ase`, `.md3` and `.skin` files aren't parsed to see which textures and shaders they use.
- `terrain` shaders are not supported.
