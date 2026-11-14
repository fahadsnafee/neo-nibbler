# Neo Nibbler

A synthwave desktop droid that chases file icons, scans for configured extensions, and sorts matching files into folders you choose.

Extract the ZIP and run NeoNibbler.exe on Windows 11 x64. The executable includes .NET. The synthwave droid is the only window shown at launch. Single- or double-click the droid to scan once, right-click it for Settings, or drag it to reposition it. Settings lets you turn on or off chasing matching desktop icons, turn on or off random mouse chases, and choose a 5- or 10-second mouse chase. Chasing uses animation only and does not move the mouse pointer.

Right-click the droid icon in the Windows notification area for **Scan Desktop**, **Settings**, **About me**, **Website**, and **Exit**. Windows may place the icon under the notification-area overflow arrow.

## Choose how the droid sorts

1. In **Settings**, enter an extension such as **.pdf**, choose its folder with **Browse**, and click **Save rule**.
2. **Filename tooltips** applies to every extension you add. With desktop icons visible, the laser targets each matching file and shows its full filename beneath its icon. If Explorer cannot identify an icon, the callout appears near the droid.
3. **Auto-move matches** moves files with saved rules after the scan identifies them. With this option off, use **Move checked** in Settings after reviewing the list.
4. **Watch Desktop** checks for new or changed matches in the background. With watching off, use **Scan Desktop** on demand. Both automatic options are off on first launch. Settings take effect on the next scan.

The scan follows your Windows Desktop location and checks files directly in that folder. A numbered suffix protects existing files with the same name. Files modified between discovery and move are skipped. Settings are stored in %LOCALAPPDATA%\AstromechDesk.

## Build from source

Install the .NET 10 SDK. In the windows folder, run:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```
