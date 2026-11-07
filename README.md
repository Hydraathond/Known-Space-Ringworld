# Known Space: Ringworld

A RimWorld mod that lets you play on a ringworld, as described in Larry Niven's *Ringworld* books: a band of land
circling a sun, with rim walls on both edges and shadow squares that bring night.

## Requirements

- RimWorld 1.7
- [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077)

## Installing

1. Download `Ringworld-<version>.zip` from the [latest release](https://github.com/Hydraathond/Known-Space-Ringworld/releases/latest).
2. Unzip it into your RimWorld `Mods` folder, so you get `Mods/Ringworld/About/About.xml`.
3. Turn on Harmony and Ringworld in the mod list, with Harmony first.

## Playing

On the Create World page, pick **Ring** as the world shape, choose a ring size and land coverage, and play as normal.
Pick **Planet** to get the vanilla globe.

Works with [Layered Atmosphere and Orbit](https://steamcommunity.com/workshop/filedetails/?id=3568552579): load
Ringworld after it.

## Building from source

The C# source is in `Source/Ringworld`. Build it with the .NET SDK:

```
cd Source/Ringworld
dotnet build -c Release
```

This writes `Assemblies/Ringworld.dll`. If RimWorld or the Harmony workshop mod are installed somewhere else, pass
`-p:RimWorldDir="..."` and `-p:HarmonyDir="..."` (see `Ringworld.csproj`).

## AI Disclaimer

Code was mostly written by AI.
Any artwork and textures were made by a human.

## License

[CC BY-NC-SA 4.0](LICENSE): you may share and adapt this mod, including patches and forks, as long as you give credit,
don't use it commercially, and share your changes under the same license. Please don't re-upload it to the Steam
Workshop as-is.

Streamers and video creators are welcome. For the purposes of Section 8(c) of the License, streamers and video
creators (for example on Twitch and YouTube) are expressly permitted to publicly perform and display this mod for
commercial advantage or monetary compensation, such as monetised streams and videos, provided all other terms of the
License are met. This permission ends if the License is terminated under Section 6(a).

*Ringworld* and *Known Space* are the work of Larry Niven. This is an unofficial fan mod.
