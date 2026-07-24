namespace VeloPic.Core;

public enum ImageOrientation
{
    All,
    Landscape,
    Portrait
}

public static class ImageOrientationFilter
{
    public static bool Matches(int width, int height, ImageOrientation orientation)
    {
        return orientation switch
        {
            ImageOrientation.Landscape => width > height,
            ImageOrientation.Portrait => height > width,
            _ => true
        };
    }
}
