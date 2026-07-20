namespace VeloPic.Core;

public sealed record ScanOptions(
    string RootPath,
    bool Recursive = true,
    bool IncludeHidden = true,
    bool IncludeSystem = false);
