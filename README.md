# Silence Cutter Timeline - Avalonia + FFmpeg

A cross-platform desktop app that detects speech pauses in multiple video clips, shows an editable pause timeline, and exports:

- a rendered cut video with selected pauses removed
- pause-only clips
- EDL pause markers for DaVinci Resolve
- CSV cut list for debugging/post-processing

## Why this approach

DaVinci Resolve scripting can hit limitations in the free version. This app does the hard part outside Resolve by using FFmpeg's `silencedetect` filter, then lets you manually choose what to remove before exporting.

## Requirements

- .NET 8 SDK
- FFmpeg, FFprobe, and FFplay installed and available in PATH

Check:

```bash
ffmpeg -version
ffprobe -version
ffplay -version
```

## Run

```bash
dotnet restore
dotnet run
```

## Suggested settings

For voice recordings:

- Threshold: `-35 dB`
- Minimum pause: `0.45s`
- Padding: `0.08s`

If it cuts breathing or quiet words, lower sensitivity by using `-30 dB` or increase minimum pause to `0.60s`.
If it misses pauses, use `-40 dB` or lower minimum pause to `0.30s`.

## Resolve workflow

### Option A: Fastest
Use **Export Cut Video** and import the rendered file into Resolve.

### Option B: Review pauses in Resolve
Use **Export EDL Markers** and import the EDL as markers. Depending on your Resolve version, import marker EDL from the timeline/media pool context menu.

### Option C: Manual/advanced
Use **Export CSV Cut List**. It contains exact pause start/end times and whether each pause was marked for removal.

## Notes

- Frame-accurate cutting usually requires re-encoding. Keep `Re-encode exports` enabled for reliable results.
- Stream copy mode is faster, but cuts only around keyframes and can be slightly inaccurate.
- The app currently processes one selected clip at a time for export. Multi-clip batch export can be added easily by iterating over analyzed clips.
