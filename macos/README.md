# Neo Nibbler for macOS

Use the Apple Silicon build on M-series Macs and the Intel build on x86-64 Macs. Both bundles include .NET and target macOS 14 or later.

Open Neo Nibbler.app to show the desktop droid and the menu bar icon. Left-click the droid to scan or right-click for Settings. Add a file extension and choose its destination. Enable Auto-move matches to sort automatically, or use Move checked after reviewing a scan.

Allow Desktop folder access if macOS asks. Finder automation permission lets the droid locate desktop icons for chasing and tooltips. If Finder does not return a position, the filename appears near the droid. Settings are stored in ~/Library/Application Support/NeoNibbler.

## Build on a Mac

Install the .NET 10 SDK. From the repository root:

```sh
bash macos/package.sh osx-arm64
bash macos/package.sh osx-x64
```

The script writes the app bundle and ZIP under macos/dist. On macOS it applies an ad hoc signature for local testing. Public distribution builds still require Developer ID signing and notarization. The Finder integration and overlay need a live macOS check.
