<p align="center">
  <img
    width="256"
    height="305"
    title="Zip format icons created by juicy_fish - Flaticon"
    src="https://github.com/ovska/Pack3r/assets/68028366/5c628e71-bf3f-47e6-9a95-963144fcaa3e" />
  <h1 align="center">Pack3r</h1>
  <h3 align="center">Create release-ready Wolfenstein: Enemy Territory pk3 archives quickly from .map-files</h3>
</p>
---

## Features

- Parses the map file, shaders, mapscript, etc. and discovers files are needed to play the map. Editorimages, misc_models etc. are left out of the archive (unless wanted)
- Everything is performed in memory without creating any intermediate files, or modifying the originals
- Support for renaming the map to create release versions such as `b1`, with automatic renaming of bsp, mapscript, levelshots, arena and more, while the original files are left untouched
- Extensive logging to trace why each file was included in the pk3, including the exact line/byte offset where in the file the shader/file was referenced.
- Support for file discovery from pk3's and pk3dirs, such as `sd-mapobjects.pk3` or a texture/model pack extracted into a separate pk3dir to keep etmain clean
- Warnings about possible pitfalls such as lightmaps from another compile, or image/audio formats not supported by ET 2.60b

---

## Usage
`Pack3r <map> [options]`

### Arguments:
`<map>`  Path to the .map file [required]

### Options:
#### `-o, --output` `<output file or dir name>`
Destination of the packing, defaults to `etmain`. Possible paths are filenames with `pk3` or `zip` extension, or directories.
File name in case of directory is `mapname.pk3`, or `.zip` if using `--source`. This setting is ignored if using `--dryrun`.
Example: `-o C:/ET/mapreleases`

#### `-d, --dryrun`
Run the packing operation without actually creating an archive.
Useful if you just want to discover what files would be packed or are missing, or want to see the size of the pk3.

#### `-r --rename` `<output map/pk3 name>`
Name of the map after packing. Can be used to create different versions without changing project names, e.g. `mapname_b1`.
Among things renamed are BSP, mapscript, lightmap folder, levelshots files and shaders.

#### `-v --verbosity` `<level>`
The threshold for log messages to be printed. Default is `Info`, which may print too much or too little information depending
on your needs. Available options (from least to most verbose): `None`, `Fatal`, `Error`, `Warn`, `Info`, `Debug`, `Trace`

#### `-l, --loose`
Creates the archive even if some files are missing. By default, missing files cause an error and don't result
in a created file. Use this setting with care if you know some files are fine to be missing.

#### `-s, --source`
Pack a zip archive of source files instead of a map release, includes files such as .map, editorimages, misc_models, etc.

#### `-f, --force`
Writes the output file even if it already exists. By default Pack3r doesn't overwrite existing pk3/zip files.

#### `-m --mods` `<one or more mod directories>`
Includes pk3s from mod folders when scanning for assets. Useful for things like tracemaps and speakerscripts that are
created in fs_game directory. Example: `--mods etjump_dev`

#### `-sd, --shaderdebug`
Prints detailed information about which shaders are required by the map, and where they are referenced (at least `--verbosity Debug` needed)

#### `-rd, --referencedebug`
Prints detailed information about which files are required by the map, and where they are referenced (at least `--verbosity Info` needed)

#### `-p, --pk3, --includepk3`
Scan pk3 files and pk3dir-directories in etmain when indexing files (off by default for performance reasons).

#### `-ns --noscan` `<one or more pk3s or dirs>`
Don't scan these pk3s/directories at all when indexing files. Example: `--noscan skies_MASTER.pk3`

#### `-np --nopack` `<one or more pk3s or dirs>`
Scan these pk3s/directories, but don't pack their contents. Example: `--nopack pak0.pk3 pak0.pk3dir`

#### `-?, -h, --help`
Prints help about usage and possible options, and their default values

#### `--version`
Prints the build version, include this in bug reports


## Limitations
- Usable only through CLI
- Only brush primitives map format is supported (NetRadiant default)
- Shaders/textures are coarsely parsed from `ase`, `md3`, `mdc`, `skin` files. Other model formats such as `obj` are not yet supported (open an issue). Models created by esoteric tools might not be parsed correctly even though radiant supports them
- `terrain` shaders (1to2 etc) are not supported (open an issue)
- For performance reasons only a subset of file extensions are packed: `tga` `jpg` `md3` `mdc` `mdm` `ase` `obj` `fbx` `shader` `wav` `roq` `skin`

## File priority order
1. `pak0.pk3` (and other `--nopack` pk3s/directories), if a file/shader is found there, it won't be included in the release
2. Files inside the _relative_ `etmain` of your map file (directory contaning `/maps`)
3. `etmain`, if the map is for example in `some.pk3dir/maps/mymap.map`
4. `pk3dir`-folders in `etmain`, in reverse alphabetical order
BSP, lightmaps, mapscript, speakerscript, soundscript, etc. are always assumed to be in the same _relative_ etmain as your map file.

#### Example
Map is `etmain/void.pk3dir/maps/void_b1.map`, the priority is:
  `pak0.pk3` -> `etmain/void.pk3dir/` -> `etmain/` -> any pk3 files in etmain
Mapscript must be in `etmain/void.pk3dir/maps/` in this case and not directly in etmain

## Example usage

### Pack a release-ready archive to `C:\ET\etmain\mymap.pk3`
```bash
.\Pack3r 'C:\ET\etmain\maps\mymap.map'
```

### Pack a release-ready archive to `C:\ET\etmain\mymap_b1.pk3` with renamed bsp
```bash
.\Pack3r 'C:\ET\etmain\maps\mymap.map' -r mymap_b1
.\Pack3r 'C:\ET\etmain\maps\mymap.map' --rename mymap_b1
```

### See if there are missing files, and how large pk3 would be created
```bash
.\Pack3r 'C:\ET\etmain\maps\mymap.map' -d
.\Pack3r 'C:\ET\etmain\maps\mymap.map' --dryrun
```

### Figure out what files were included and why (without creating pk3)
```bash
.\Pack3r 'C:\ET\etmain\maps\mymap.map' -d -rd -v info
.\Pack3r 'C:\ET\etmain\maps\mymap.map' --dryrun --referencedebug --verbosity info
```

### Share map source with someone else
```bash
.\Pack3r 'C:\ET\etmain\maps\mymap.map' -s -l -o 'C:\mymap_source.zip'
.\Pack3r 'C:\ET\etmain\maps\mymap.map' --source --loose --output 'C:\mymap_source.zip'
```

### Pack a map while ignoring some pk3s in etmain and including others
```bash
.\Pack3r 'C:\ET\etmain\maps\mymap.map' --pk3 --noscan skies_MASTER.pk3
```
