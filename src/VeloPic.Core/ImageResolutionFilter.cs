namespace VeloPic.Core;

public enum ImageResolutionLevel
{
    All,
    OneK,
    TwoK,
    FourK
}

public static class ImageResolutionFilter
{
    public static bool Matches(int width, int height, ImageResolutionLevel level)
    {
        if (level == ImageResolutionLevel.All)
        {
            return true;
        }

        var longEdge = Math.Max(width, height);
        var shortEdge = Math.Min(width, height);
        return level switch
        {
            ImageResolutionLevel.FourK => longEdge >= 3840 && shortEdge >= 2160,
            ImageResolutionLevel.TwoK => longEdge >= 2560 && shortEdge >= 1440,
            ImageResolutionLevel.OneK => longEdge >= 1920 && shortEdge >= 1080,
            _ => true
        };
    }
}
