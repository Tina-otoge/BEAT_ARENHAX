# BEAT ARENHAX

<p align="center">
  <img src=".github/readme/title.png" alt="BEAT ARENHAX logo">
</p>

A mod to inject custom DTX charts in the VR rhythm game "BEAT ARENA", and a
variety of other quality of life changes.

This mod is made with only the drum instrument in mind. Attempting to play a
custom song with any of the other instruments will load a default chart instead.

[![Watch BEAT ARENHAX on YouTube](https://markdown-videos-api.jorgenkh.no/youtube/xeZMw7vO-RA?width=480&height=270)](https://www.youtube.com/watch?v=xeZMw7vO-RA)

## Available patches

- Load custom songs, using the DTX chart format. Recursive folder parsing and
  #RANDOM is supported.
- Skip the "Reboot or start offline" popup on startup (servers are closed)
- Skip the KONAMI logo and title logo screens (make it a little bit faster to
  start)
- Skip the "caution with the controllers..." popups. Not recommended, this make
  you drop in the lobby before it loads, making object pop and flash, which is a
  bit uncomfortable to see in VR. So I recommend not using this patch.
- Skip the "wave to your partners before starting" ceremony. It's cute and funny
  the first times you do it, I definitely recommend playing the game unmodded
  for your first few songs so you can experience it. But after a while having to
  wave to NPCs feel a bit uncanny.
- Skip the "send your impression to the members" phase after a song, so you can
  see your score immediately. Normally you have to wait either 12 seconds, or
  pick emotes ("kawaii", "exciting", ...) for each of your partners before your
  score appears.
- Force unlock avatar parts
- Force unlock campaign rewards
- Change player name
- Change audio volume for BGM and drums separately
- Change WASAPI buffer latency
- Change offset

## How to install

Download the [latest release](releases/latest) and extract to the game
directory.

Running the game successfully with the plugin loaded will show several logs
starting with `[<severity>: BeatArenhax]`. The plugin will create
`beat_arenhax.ini` on first run. Review and modify this file to your
preferences.

By default, custom songs go to a folder named `DTXFiles`. You can update
`CustomSongs.Path` in `beat_arenhax.ini` to point to your existing DTXMania
installation if you have one. This plugin will not write content inside it.

Since the game only has "Casual" and "Advanced" difficulties, only 2 charts per
set are loaded. Regular DTX sets support up to 5 difficulties. The setting
`CustomSongs.DifficultiesCut` supports the values "easiest" and "hardest". This
way you can either pick the 2 bottom difficulties, or the 2 top difficulties.

### Linux / Steam Frame / Proton

In Steam, open the game's **Properties > Launch Options** and set:

```sh
WINEDLLOVERRIDES="winhttp=n,b" %command%
```

### Building manually

If you want to edit the code and/or build the plugin manually, follow these
steps:

1. Make sure the .NET 6+ SDK is available (see `dotnet sdk check`)
2. Download and extract the Windows x64 IL2CPP build of [BepInEx 6](https://docs.bepinex.dev/articles/user_guide/installation/index.html) into the game directory.
3. Launch the game once and wait for BepInEx to generate `BepInEx/interop`. Close the game after BepInEx reports that chainloader startup is complete.
4. Run `dotnet build BeatArenhax/BeatArenhax.csproj -c Release -p:GameDir="<game directory>"`. Optionally, add `-p:BeatArenhaxVersion=X.X.X` where X.X.X = the name you want for this build.
5. Copy `BeatArenhax/bin/Release/net6.0/*` into `<game directory>/BepInEx/plugins/BeatArenhax`

## AI-use disclosure

TLDR, I vibecoded the initial prototype in 1 hour, then spent 2 days cleaning it
with my two human hands.

The initial version of the mod was made by blindly throwing an AI at it until it
worked. All of the prompts and AI answers for building the V1 are public and can
be read in [AI_SESSION.md](docs/AI_SESSION.md). Model used is Claude Opus 5.5.
This is also the version you see in the YouTube video.

I then spent a while redesigning the architecture and style by hand, now that I
could trust the "core logic" to work. I am not new to modding with BepInEx, but
this is my first time writing a plugin from scratch, and there was a lot that I
didn't really want to bother with. I have little to no experience in reverse
engineering / inspecting, so AI was a big help on that part. The initial
vibecoded slop helped me get a working base and then iterate on it until the
codebase was in a state I like.

The V1 was built using VS Code's Agents Window in Assisted permissions mode, the
first 2 rounds took 1 hour and 34 min. Then I spent 2 hours doing back and
forths with the AI adding more things I wanted and fixing stuff. After that, I
started working manually on the code, mostly to clean it / make it more into my
style. Basically "unsloppifying" it. Most patches were in a single 800-lines
messy file, my first step was redesigning the plugin into seperate modules.

## License

MIT.
