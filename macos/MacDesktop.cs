using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;

namespace NeoNibblerMac;

static class MacDesktop
{
    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Point(double X, double Y);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern IntPtr CGEventCreate(IntPtr source);
    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern Point CGEventGetLocation(IntPtr evt);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr value);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern IntPtr sel_registerName(string name);
    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern void objc_msgSend(IntPtr receiver, IntPtr selector, byte value);

    public static void MakeClickThrough(IntPtr window)
    {
        if (OperatingSystem.IsMacOS())
            objc_msgSend(window, sel_registerName("setIgnoresMouseEvents:"), 1);
    }

    public static PixelPoint? Cursor()
    {
        if (!OperatingSystem.IsMacOS()) return null;
        IntPtr evt = CGEventCreate(IntPtr.Zero);
        if (evt == IntPtr.Zero) return null;
        try
        {
            var point = CGEventGetLocation(evt);
            return new PixelPoint((int)point.X, (int)point.Y);
        }
        finally { CFRelease(evt); }
    }

    public static async Task<Dictionary<string, PixelPoint>> Icons()
    {
        var positions = new Dictionary<string, PixelPoint>(StringComparer.Ordinal);
        if (!OperatingSystem.IsMacOS()) return positions;
        var start = new ProcessStartInfo("/usr/bin/osascript") { RedirectStandardOutput = true, UseShellExecute = false };
        start.ArgumentList.Add("-e");
        start.ArgumentList.Add("""
            with timeout of 4 seconds
                tell application "Finder"
                    set rows to {}
                    repeat with itemRef in (every file of desktop)
                        try
                            set {x, y} to desktop position of itemRef
                            set end of rows to ((x as text) & tab & (y as text) & tab & name of itemRef)
                        end try
                    end repeat
                end tell
                set AppleScript's text item delimiters to linefeed
                return rows as text
            end timeout
            """);
        using var process = Process.Start(start);
        if (process is null) return positions;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        try
        {
            string output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            foreach (string line in output.Split('\n'))
            {
                string[] parts = line.Split('\t', 3);
                if (parts.Length == 3 && int.TryParse(parts[0], out int x) && int.TryParse(parts[1], out int y))
                    positions[parts[2]] = new PixelPoint(x, y);
            }
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill();
        }
        return positions;
    }
}
