# Pack3r

Create release-ready pk3 archives quickly. Pack3r uses Radiant `.map`-files to read shaders, models, mapscripts, etc. for asset discovery and includes only files required to play the map in the pk3.

## Usage
`Pack3r <map> [options]`

### Arguments:
`<map>`  Path to the .map file [required]

### Options:
- `-o, --output` Path to destination pk3 or directory, defaults to etmain
- `-d, --dryrun` Discover packed files and estimate file size without creating a pk3 [default: False]
- `-r, --rename` Map release name (bsp, lightmaps, mapscript, etc.)
- `-v, --verbosity` Log severity threshold [default: Info]
- `-l, --loose` Complete packing even if some files are missing [default: False]
- `-s, --source` Pack source files such as .map, editorimages, misc_models [default: False]
- `-sl, --shaderlist` Only read shaders present in shaderlist.txt [default: False]
- `-sd, --shaderdebug` Print shader resolution details (Debug verbosity needed) [default: False]
- `-rd, --referencedebug` Print asset resolution details (Info verbosity needed) [default: False]
- `-f, --force` Overwrite existing files in the output path with impunity [default: False]
- `-i, --includepk3` Include pk3 files and pk3dirs in etmain when indexing files [default: False]
- `--ignore` Ignore some pk3 files or pk3dir directories [default: pak1.pk3|pak2.pk3|mp_bin.pk3]
- `-e, --exclude` Never pack files found in these pk3s or directories [default: pak0.pk3|pak0.pk3dir]
- `-?, -h, --help` Show help and usage information
- `-v, --version` Show version information

Example:

```bash
.\Pack3r.exe 'C:\Temp\ET\map\ET\etmain\maps\sungilarity.map' --o 'C:\Temp\test.pk3'
.\Pack3r.exe 'C:\Temp\ET\map\ET\etmain\maps\sungilarity.map' --dryrun --includepk3
```

## Limitations
- Only brush primitives map format is supported (NetRadiant default)
- Shaders/textures are parsed from `ase`, `md3`, `mdc` (and `skin`) files. Other model formats such as `obj` are not yet supported (open an issue).
- `terrain` shaders (1to2 etc) are not supported (open an issue)
- `--rename` does not yet work for levelshots-shaders. The `levelshots/mapname.tga/jpg` file is renamed correctly though

## File priority order
1. `pak0.pk3` (and other `--exclude` pk3s/directories), if a file or shader is found there, it won't be included in the release
2. Files inside the _relative_ `etmain` of your map file (directory contaning `/maps`)
3. `etmain`, if the map is for example in `some.pk3dir/maps/mymap.map`
4. `pk3dir`-folders in `etmain`, in reverse alphabetical order
BSP, lightmaps, mapscript, speakerscript, soundscript, etc. are always assumed to be in the same _relative_ etmain as your map file.

#### Example
Map is `etmain/void.pk3dir/maps/void_b1.map`, the priority is:
  `pak0.pk3` -> `etmain/void.pk3dir/` -> `etmain/` -> any pk3 files in etmain
Mapscript must be in `etmain/void.pk3dir/maps/` in this case and not directly in etmain
