namespace VeloPic.Core;

public sealed record ImageRecord(
    string FullPath,
    string FileName,
    string DirectoryPath,
    long SizeBytes,
    DateTimeOffset ModifiedAt);
