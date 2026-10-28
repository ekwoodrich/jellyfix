# Agent notes — jellyfix

Jellyfin plugin that repairs Matroska files whose `Cues` (seek index) is only referenced from a
secondary `SeekHead`, which makes ExoPlayer-based Jellyfin clients treat the stream as
unseekable. Background and user-facing documentation live in `README.md`; this file records
what an agent needs to work on the repo.

## Quick commands

```sh
# System dotnet is 8.0 and cannot build this project; the .NET 9 SDK lives here:
export PATH="$HOME/.dotnet:$PATH"

dotnet build -c Release
dotnet test
JELLYFIX_SAMPLE="/path/to/real.mkv" dotnet test --filter RealFileSmokeTests
```

## Layout

| Path | Purpose |
|---|---|
| `Jellyfin.Plugin.JellyFix/Matroska/` | EBML primitives, `SeekIndexInspector` (detect + patch) |
| `Jellyfin.Plugin.JellyFix/Services/SeekIndexFixService.cs` | library walk, logging, `report.log` writer |
| `Jellyfin.Plugin.JellyFix/Tasks/FixSeekIndexTask.cs` | `IScheduledTask`, default trigger 24 h |
| `Jellyfin.Plugin.JellyFix/Library/PostScanSeekIndexTask.cs` | `ILibraryPostScanTask` hook |
| `Jellyfin.Plugin.JellyFix.Tests/` | xunit tests over synthetic Matroska files |

## Things that bite

* **Strict analyzers.** `TreatWarningsAsErrors` + `AnalysisMode=AllEnabledByDefault` +
  StyleCop/Serilog/Multithreading analyzers. `.editorconfig` (copied from the official
  template) downgrades most rules, but CA1819 (array properties), CA2014 (stackalloc in a
  loop), CA1860 (`Any()` on collections), CA2254 (non-constant log templates), CA1305
  (culture-sensitive formatting) and friends fail the build. Keep `this.`-free style, file
  scoped namespaces, sorted usings, XML doc text ending in a period.
* **`InternalsVisibleTo`** is declared in the plugin csproj so tests can reach the internal
  `Matroska` types. Keep the test assembly name `Jellyfin.Plugin.JellyFix.Tests`.
* **The patch must stay length preserving.** Only the 4 byte `SeekID` and the existing
  `SeekPosition` payload are overwritten; never change an element's size or move data.
  `CuesPositionThatDoesNotFitIsUnsupported` covers the case where the new offset does not fit
  the existing field width.
* **Verify the target, not just the reference.** `IsCuesElementAt` checks that the offset a
  `SeekHead` entry points at really is a `Cues` element; without it a truncated position
  silently produced a "fixed" file pointing at garbage (that was a real test failure).
* **Health check idiom**: the first `SeekHead` must contain an entry whose id is
  `0x1C53BB6B`. `mkvmerge -o out.mkv in.mkv` also fixes files, and is the recommended manual
  fallback for anything this plugin reports as unfixable.

## Testing against the real server

Target server: Jellyfin 10.11.11 in Docker on the NAS (`ssh nas`), config at
`/volume2/Storage_SSD/Docker/stremio/config` (mounted as `/config`).

```sh
dotnet build -c Release
ssh nas 'bash -s' <<'SH'
set -e
P=/volume2/Storage_SSD/Docker/stremio/config/plugins/JellyFix_1.0.0.0
sudo mkdir -p "$P"
SH
cat Jellyfin.Plugin.JellyFix/bin/Release/net9.0/Jellyfin.Plugin.JellyFix.dll \
  | ssh nas 'cat > /tmp/JellyFix.dll'
ssh nas 'bash -s' <<'SH'
set -e
P=/volume2/Storage_SSD/Docker/stremio/config/plugins/JellyFix_1.0.0.0
sudo mkdir -p "$P"
sudo cp /tmp/JellyFix.dll "$P/"
sudo chmod 644 "$P/JellyFix.dll"
docker restart jellyfin
SH
```

Then confirm `Loaded plugin: JellyFix` in `docker logs jellyfin`. Plugin configuration is
written to `/config/config/plugins/Jellyfin.Plugin.JellyFix.xml`, and the run report to
`/config/data/jellyfix/report.log`.

### NAS/SSH gotchas (cost hours if forgotten)

* `ssh nas "<command with (parentheses) or 'single quotes'>"` gets mangled by the local shell
  wrapper. Always pipe a script: `ssh nas 'bash -s' < script.sh`, and inside that script use
  **double quotes only** — single quotes are stripped before reaching the remote.
* `/volume1/Storage` is exported with `all_squash, anonuid=1000, anongid=10`. Files owned by
  `xynstore` (uid 1001) with mode `0700` are unreadable over NFS (intermittent `EACCES`) and
  cannot be `chmod`ed from a client; use `ssh nas` + `sudo`.
* `scp` to `/tmp` on the NAS fails; pipe through `ssh nas 'cat > file'` instead.

## History / context

The defect was found while fixing `Sharpe (1993)` season 1–7 on the same server: 16 episodes
had this layout, `S03E03` and the extras did not. They were repaired with `mkvmerge` before
this plugin existed; a header scan of all 2202 Matroska files under `/mnt/storage/Media/TV`
found no other affected files. That remux approach is why `README.md` documents
"12 bytes, no remux" as the plugin's advantage over the manual fix.
