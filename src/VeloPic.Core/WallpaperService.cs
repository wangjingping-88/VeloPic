using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32;

namespace VeloPic.Core;

[SupportedOSPlatform("windows")]
public sealed class WallpaperService
{
    private const int SpiSetDesktopWallpaper = 20;
    private const int SpiGetDesktopWallpaper = 0x0073;
    private const int SpifUpdateIniFile = 0x01;
    private const int SpifSendChange = 0x02;

    public void SetWallpaper(string imagePath, string fitMode = "fill")
    {
        if (!File.Exists(imagePath))
        {
            throw new FileNotFoundException("图片不存在，无法设置壁纸。", imagePath);
        }

        ApplyFitMode(fitMode);
        var ok = SystemParametersInfoSet(SpiSetDesktopWallpaper, 0, imagePath, SpifUpdateIniFile | SpifSendChange);
        if (!ok)
        {
            throw new InvalidOperationException($"设置壁纸失败，Win32 错误码：{Marshal.GetLastWin32Error()}");
        }
    }

    public DesktopWallpaperState GetCurrentWallpaper()
    {
        using var desktopKey = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
        var imagePath = ReadWallpaperPath(desktopKey);
        var wallpaperStyle = desktopKey?.GetValue("WallpaperStyle") as string;
        var tileWallpaper = desktopKey?.GetValue("TileWallpaper") as string;

        return new DesktopWallpaperState(imagePath, ResolveFitMode(wallpaperStyle, tileWallpaper));
    }

    private static string? ReadWallpaperPath(RegistryKey? desktopKey)
    {
        var pathBuffer = new StringBuilder(32768);
        if (SystemParametersInfoGet(SpiGetDesktopWallpaper, pathBuffer.Capacity, pathBuffer, 0))
        {
            var systemPath = pathBuffer.ToString();
            if (File.Exists(systemPath))
            {
                return systemPath;
            }
        }

        var registryPath = desktopKey?.GetValue("WallPaper") as string;
        if (File.Exists(registryPath))
        {
            return registryPath;
        }

        var transcodedWallpaper = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft",
            "Windows",
            "Themes",
            "TranscodedWallpaper");
        return File.Exists(transcodedWallpaper) ? transcodedWallpaper : null;
    }

    private static string ResolveFitMode(string? wallpaperStyle, string? tileWallpaper)
    {
        if (string.Equals(tileWallpaper, "1", StringComparison.Ordinal))
        {
            return "tile";
        }

        return wallpaperStyle switch
        {
            "0" => "center",
            "2" => "stretch",
            "6" => "fit",
            "22" => "span",
            _ => "fill"
        };
    }

    private static void ApplyFitMode(string fitMode)
    {
        var (wallpaperStyle, tileWallpaper) = fitMode.ToLowerInvariant() switch
        {
            "fit" => ("6", "0"),
            "center" => ("0", "0"),
            "tile" => ("0", "1"),
            "stretch" => ("2", "0"),
            _ => ("10", "0")
        };

        using var desktopKey = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop");
        desktopKey?.SetValue("WallpaperStyle", wallpaperStyle, RegistryValueKind.String);
        desktopKey?.SetValue("TileWallpaper", tileWallpaper, RegistryValueKind.String);
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfoSet(int action, int param, string value, int flags);

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfoGet(int action, int param, StringBuilder value, int flags);
}

public sealed record DesktopWallpaperState(string? ImagePath, string FitMode);
