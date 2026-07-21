namespace VeloPic.Core;

public sealed record MediaRecord(
    string FullPath,
    string FileName,
    string DirectoryPath,
    long SizeBytes,
    DateTimeOffset ModifiedAt,
    MediaKind Kind)
{
    public ImageRecord AsImageRecord()
    {
        if (Kind != MediaKind.Image)
        {
            throw new InvalidOperationException("视频记录不能转换为图片记录。");
        }

        return new ImageRecord(FullPath, FileName, DirectoryPath, SizeBytes, ModifiedAt);
    }
}
