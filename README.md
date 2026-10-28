# JellyFix

A Jellyfin plugin that repairs Matroska files whose **seek index cannot be reached**, which is
why some videos restart from the beginning whenever you try to scrub in ExoPlayer-based
clients (Jellyfin for Android TV, Wholphin, Findroid, Streamyfin...).

VLC and MPV handle these files fine, and the video itself is not damaged. Only the container
index is laid out in a way ExoPlayer refuses to follow.

## The bug

Matroska stores a seek index in a `Cues` element and points at it with a `SeekHead`. Some
muxers (a number of 2018-era `mkvmerge` remuxes, for example) write the header like this:

```
Segment
├── SeekHead #1        ← one entry, pointing at... SeekHead #2
├── Info
├── Tracks
├── Chapters
├── Cluster …
└── SeekHead #2        ← the only place that references Cues
    Cues
```

ExoPlayer's Matroska extractor reads the first `SeekHead` only. Because that entry points at
another `SeekHead` instead of `Cues`, it never learns where the index is, concludes
`current media item is not seekable`, and the client restarts the stream from position 0 on
every seek.

This is [jellyfin/jellyfin-androidtv#5221](https://github.com/jellyfin/jellyfin-androidtv/issues/5221),
upstream [androidx/media#1143](https://github.com/androidx/media/issues/1143). Known
workarounds are remuxing the file (`mkvmerge -o out.mkv in.mkv`) or transcoding on the server.

## The repair

JellyFix rewrites **only that one `SeekHead` entry**, retargeting it from `SeekHead #2` to the
real `Cues` element:

```
- 11 4D 9B 74   (SeekID  = SeekHead)
+ 1C 53 BB 6B   (SeekID  = Cues)
- 00 00 00 00 …  (SeekPosition = offset of SeekHead #2)
+ 00 00 00 00 …  (SeekPosition = offset of Cues)
```

* **12 bytes written, nothing else moved.** No remux, no re-encode, no temp copy, no media
  data touched. A 6 GB episode is repaired in milliseconds.
* Every repair is verified by re-reading the file before it is reported as fixed.
* The Cues target is validated (the element at that offset must really be `Cues`) so a
  malformed file can never be pointed at garbage.
* Files broken in other ways are reported as *unfixable* with a reason instead of being
  modified — for those, remux with `mkvmerge` and re-run the task.

## Installation

1. Build or download `Jellyfin.Plugin.JellyFix.dll`.
2. Create a folder in your server's plugin directory and copy the DLL into it:

   ```sh
   mkdir -p /path/to/config/plugins/JellyFix_1.0.0.0
   cp Jellyfin.Plugin.JellyFix.dll /path/to/config/plugins/JellyFix_1.0.0.0/
   ```

   For the official Docker image that is `/config/plugins/JellyFix_1.0.0.0/`
   (`/volume2/Storage_SSD/Docker/stremio/config/plugins/...` on the NAS this was built for).
3. Restart Jellyfin. The server log shows `Loaded plugin: JellyFix 1.0.0.0`.
4. Configure it under **Dashboard → Plugins → JellyFix**.

## Settings

| Setting | Default | Meaning |
|---|---|---|
| Enable JellyFix | on | Master switch for both the task and the post-scan action. |
| Repair files | on | Off = dry run. Files are inspected and reported, never written. |
| Run after every library scan | off | Also repair at the end of each library scan. |
| Skip files that are currently playing | on | Never patch a file that has an active playback session. |
| Excluded path prefixes | empty | One prefix per line; matching paths are ignored. |

## Running it

* **Scheduled task** — *Dashboard → Scheduled Tasks → "Repair Matroska seek indexes"*.
  Ships with a 24 hour interval trigger; edit the trigger there (or run it manually with the
  play button) at any time.
* **After a library scan** — enable *Run after every library scan* in the plugin settings.

Only files Jellyfin has indexed as video items with a `.mkv`/`.webm` extension are inspected,
so the cost is one small header read per file.

## Where results go

1. The normal Jellyfin log, one line per action:

   ```
   [INF] JellyFix: repaired /media/tv/Show/Episode.mkv (Retargeted the first SeekHead entry to Cues; Cues found at segment offset 5903883165.)
   [WRN] JellyFix: cannot repair /media/tv/Show/Other.mkv: The first SeekHead entry id is not 4 bytes wide, so it cannot be retargeted to Cues.
   [INF] JellyFix finished: 2202 scanned, 2197 already fine, 5 repaired, 0 repairable but not fixed, 0 not repairable, 0 errors, 0 skipped.
   ```
2. A tab separated report that survives log rotation, at
   `<data>/jellyfix/report.log` (`/config/data/jellyfix/report.log` in Docker), with one line
   per interesting file plus a `SUMMARY` line per run:

   ```
   2026-10-28T01:18:13.9021540Z	FIXED	/media/tv/Show/Episode.mkv	Retargeted the first SeekHead entry to Cues; …
   ```

## Safety

* The patch is a fixed-width, length-preserving edit: element sizes, offsets and all media
  data stay exactly where they were, so no offsets need rewriting.
* Already-healthy files are detected and skipped without a write (idempotent).
* Dry-run mode lets you see the impact before letting it write anything.
* Nothing is repaired while a file is being streamed, unless you turn that guard off.
* Keep normal backups of your media library; this tool writes to your files by design.

## Building

```sh
dotnet build -c Release
dotnet test
```

Requires the .NET 9 SDK. The plugin targets `net9.0` and references `Jellyfin.Controller` /
`Jellyfin.Model` `10.11.11` with `targetAbi` `10.11.0.0`.

Optional real-file smoke test (read-only):

```sh
JELLYFIX_SAMPLE="/path/to/sample.mkv" dotnet test --filter RealFileSmokeTests
```

## Project layout

```
Jellyfin.Plugin.JellyFix/
├── Plugin.cs                        plugin entry point, settings page registration
├── ServiceRegistrator.cs            DI registration
├── Configuration/                   settings model + dashboard settings page
├── Matroska/                        EBML reader, seek index inspector/patcher
├── Services/SeekIndexFixService.cs  library walk, logging, report file
├── Tasks/FixSeekIndexTask.cs        scheduled task (24 h default trigger)
└── Library/PostScanSeekIndexTask.cs post-library-scan hook
Jellyfin.Plugin.JellyFix.Tests/      xunit tests over synthetic Matroska files
```

## License

MIT
