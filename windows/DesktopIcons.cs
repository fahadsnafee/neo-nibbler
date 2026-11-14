using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace AstromechDesk;

static class DesktopIcons
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string? title);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? title);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    public static Dictionary<string, Rectangle> Locate(IEnumerable<string> desktopFiles)
    {
        var results = new Dictionary<string, Rectangle>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var view = FindView(FindWindow("Progman", null));
            var worker = IntPtr.Zero;
            while (view == IntPtr.Zero && (worker = FindWindowEx(IntPtr.Zero, worker, "WorkerW", null)) != IntPtr.Zero)
                view = FindView(worker);
            if (view == IntPtr.Zero || !IsWindowVisible(view)) return results;

            var files = desktopFiles.Select(Path.GetFileName).OfType<string>().ToArray();
            var labels = files.SelectMany(name => new[] { (Label: name, Name: name),
                    (Label: Path.GetFileNameWithoutExtension(name), Name: name) })
                .GroupBy(entry => entry.Label, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Select(entry => entry.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1)
                .ToDictionary(group => group.Key, group => group.First().Name, StringComparer.OrdinalIgnoreCase);
            var items = AutomationElement.FromHandle(view).FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
            var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (AutomationElement item in items)
            {
                var data = item.Current;
                var rect = data.BoundingRectangle;
                if (data.IsOffscreen || rect.IsEmpty || !labels.TryGetValue(data.Name, out string? file)) continue;
                var bounds = Rectangle.Round(new RectangleF((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height));
                if (!results.TryAdd(file, bounds)) ambiguous.Add(file);
            }
            foreach (string file in ambiguous) results.Remove(file);
        }
        catch (Exception ex) when (ex is COMException or ElementNotAvailableException or InvalidOperationException or UnauthorizedAccessException)
        {
            results.Clear();
        }
        return results;
    }

    private static IntPtr FindView(IntPtr host)
    {
        if (host == IntPtr.Zero) return IntPtr.Zero;
        var desktop = FindWindowEx(host, IntPtr.Zero, "SHELLDLL_DefView", null);
        return desktop == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(desktop, IntPtr.Zero, "SysListView32", null);
    }
}
