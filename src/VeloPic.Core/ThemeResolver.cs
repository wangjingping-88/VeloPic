namespace VeloPic.Core;

public static class ThemeResolver
{
    public static ResolvedTheme Resolve(VeloPicThemeMode mode, ResolvedTheme systemTheme)
    {
        return mode switch
        {
            VeloPicThemeMode.Dark => ResolvedTheme.Dark,
            VeloPicThemeMode.Light => ResolvedTheme.Light,
            _ => systemTheme
        };
    }
}
