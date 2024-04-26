# Pack3r

Create release-ready pk3 archives quickly. Pack3r uses NetRadiant `.map`-files, shaders, mapscripts and sound/speakerscripts for asset discovery and includes only files required to play the map in the pk3.

### To do
- Dry-run to see which files would be included and how big the pk3 would bee

### Limitations
- Only NetRadiant `.map` files are supported, not GTK radiant
- `.ase`, `.md3` and `.skin` files aren't parsed to see which textures and shaders they use.
- `terrain` shaders are not supported.
