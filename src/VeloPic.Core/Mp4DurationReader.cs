using System.Buffers.Binary;
using System.Text;

namespace VeloPic.Core;

public static class Mp4DurationReader
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".mov", ".3gp", ".m2ts", ".mts"
    };

    public static TimeSpan? TryRead(string path)
    {
        if (!SupportedExtensions.Contains(Path.GetExtension(path)))
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var context = new ParseContext();
            ParseTopLevel(stream, context);
            return context.GetVideoDuration();
        }
        catch
        {
            return null;
        }
    }

    private static void ParseTopLevel(Stream stream, ParseContext context)
    {
        while (TryReadBox(stream, stream.Length, out var box))
        {
            switch (box.Type)
            {
                case "moov":
                    ParseMovie(stream, box.End, context);
                    break;
                case "moof":
                    ParseMovieFragment(stream, box.End, context);
                    break;
            }

            stream.Position = box.End;
        }
    }

    private static void ParseMovie(Stream stream, long end, ParseContext context)
    {
        while (TryReadBox(stream, end, out var box))
        {
            switch (box.Type)
            {
                case "trak":
                    ParseTrack(stream, box.End, context);
                    break;
                case "mvex":
                    ParseMovieExtends(stream, box.End, context);
                    break;
            }

            stream.Position = box.End;
        }
    }

    private static void ParseTrack(Stream stream, long end, ParseContext context)
    {
        var track = new TrackInfo();
        while (TryReadBox(stream, end, out var box))
        {
            switch (box.Type)
            {
                case "tkhd":
                    track.Id = ReadTrackId(stream, box.End);
                    break;
                case "mdia":
                    ParseMedia(stream, box.End, track);
                    break;
            }

            stream.Position = box.End;
        }

        if (track.Id != 0)
        {
            context.Tracks[track.Id] = track;
        }
    }

    private static void ParseMedia(Stream stream, long end, TrackInfo track)
    {
        while (TryReadBox(stream, end, out var box))
        {
            switch (box.Type)
            {
                case "mdhd":
                    ReadMediaHeader(stream, box.End, track);
                    break;
                case "hdlr":
                    track.IsVideo = ReadHandlerType(stream, box.End) == "vide";
                    break;
            }

            stream.Position = box.End;
        }
    }

    private static void ParseMovieExtends(Stream stream, long end, ParseContext context)
    {
        while (TryReadBox(stream, end, out var box))
        {
            if (box.Type == "trex" && box.End - stream.Position >= 16)
            {
                var versionFlags = ReadUInt32(stream);
                var trackId = ReadUInt32(stream);
                _ = ReadUInt32(stream);
                var defaultDuration = ReadUInt32(stream);
                if (versionFlags <= uint.MaxValue && trackId != 0 && defaultDuration != 0)
                {
                    context.DefaultDurations[trackId] = defaultDuration;
                }
            }

            stream.Position = box.End;
        }
    }

    private static void ParseMovieFragment(Stream stream, long end, ParseContext context)
    {
        while (TryReadBox(stream, end, out var box))
        {
            if (box.Type == "traf")
            {
                ParseTrackFragment(stream, box.End, context);
            }

            stream.Position = box.End;
        }
    }

    private static void ParseTrackFragment(Stream stream, long end, ParseContext context)
    {
        uint trackId = 0;
        uint defaultDuration = 0;
        ulong? decodeTime = null;
        ulong fragmentDuration = 0;

        while (TryReadBox(stream, end, out var box))
        {
            switch (box.Type)
            {
                case "tfhd":
                    ReadTrackFragmentHeader(stream, box.End, out trackId, out defaultDuration);
                    if (defaultDuration == 0)
                    {
                        context.DefaultDurations.TryGetValue(trackId, out defaultDuration);
                    }
                    break;
                case "tfdt":
                    decodeTime = ReadDecodeTime(stream, box.End);
                    break;
                case "trun":
                    fragmentDuration += ReadTrackRunDuration(stream, box.End, defaultDuration);
                    break;
            }

            stream.Position = box.End;
        }

        if (trackId == 0 || fragmentDuration == 0)
        {
            return;
        }

        var start = decodeTime ?? context.FragmentEnds.GetValueOrDefault(trackId);
        context.FragmentEnds[trackId] = Math.Max(context.FragmentEnds.GetValueOrDefault(trackId), start + fragmentDuration);
    }

    private static uint ReadTrackId(Stream stream, long end)
    {
        if (end - stream.Position < 16)
        {
            return 0;
        }

        var versionFlags = ReadUInt32(stream);
        var version = (byte)(versionFlags >> 24);
        var offset = version == 1 ? 16 : 8;
        if (end - stream.Position < offset + 4)
        {
            return 0;
        }

        stream.Position += offset;
        return ReadUInt32(stream);
    }

    private static void ReadMediaHeader(Stream stream, long end, TrackInfo track)
    {
        if (end - stream.Position < 20)
        {
            return;
        }

        var versionFlags = ReadUInt32(stream);
        var version = (byte)(versionFlags >> 24);
        stream.Position += version == 1 ? 16 : 8;
        if (end - stream.Position < (version == 1 ? 12 : 8))
        {
            return;
        }

        track.TimeScale = ReadUInt32(stream);
        track.DeclaredDuration = version == 1 ? ReadUInt64(stream) : ReadUInt32(stream);
    }

    private static string ReadHandlerType(Stream stream, long end)
    {
        if (end - stream.Position < 12)
        {
            return string.Empty;
        }

        stream.Position += 8;
        Span<byte> type = stackalloc byte[4];
        stream.ReadExactly(type);
        return Encoding.ASCII.GetString(type);
    }

    private static void ReadTrackFragmentHeader(Stream stream, long end, out uint trackId, out uint defaultDuration)
    {
        trackId = 0;
        defaultDuration = 0;
        if (end - stream.Position < 8)
        {
            return;
        }

        var versionFlags = ReadUInt32(stream);
        var flags = versionFlags & 0x00FFFFFF;
        trackId = ReadUInt32(stream);
        if ((flags & 0x000001) != 0)
        {
            stream.Position += 8;
        }
        if ((flags & 0x000002) != 0)
        {
            stream.Position += 4;
        }
        if ((flags & 0x000008) != 0 && end - stream.Position >= 4)
        {
            defaultDuration = ReadUInt32(stream);
        }
    }

    private static ulong? ReadDecodeTime(Stream stream, long end)
    {
        if (end - stream.Position < 8)
        {
            return null;
        }

        var versionFlags = ReadUInt32(stream);
        var version = (byte)(versionFlags >> 24);
        return version == 1 && end - stream.Position >= 8
            ? ReadUInt64(stream)
            : end - stream.Position >= 4
                ? ReadUInt32(stream)
                : null;
    }

    private static ulong ReadTrackRunDuration(Stream stream, long end, uint defaultDuration)
    {
        if (end - stream.Position < 8)
        {
            return 0;
        }

        var versionFlags = ReadUInt32(stream);
        var flags = versionFlags & 0x00FFFFFF;
        var sampleCount = ReadUInt32(stream);
        if ((flags & 0x000001) != 0)
        {
            stream.Position += 4;
        }
        if ((flags & 0x000004) != 0)
        {
            stream.Position += 4;
        }

        if ((flags & 0x000100) == 0)
        {
            return (ulong)sampleCount * defaultDuration;
        }

        ulong duration = 0;
        for (var index = 0u; index < sampleCount && stream.Position + 4 <= end; index++)
        {
            duration += ReadUInt32(stream);
            if ((flags & 0x000200) != 0)
            {
                stream.Position += 4;
            }
            if ((flags & 0x000400) != 0)
            {
                stream.Position += 4;
            }
            if ((flags & 0x000800) != 0)
            {
                stream.Position += 4;
            }
        }

        return duration;
    }

    private static bool TryReadBox(Stream stream, long parentEnd, out Box box)
    {
        box = default;
        if (stream.Position + 8 > parentEnd)
        {
            return false;
        }

        var start = stream.Position;
        var size = ReadUInt32(stream);
        Span<byte> typeBytes = stackalloc byte[4];
        stream.ReadExactly(typeBytes);
        var type = Encoding.ASCII.GetString(typeBytes);
        long headerSize = 8;
        long boxSize = size;

        if (size == 1)
        {
            if (stream.Position + 8 > parentEnd)
            {
                return false;
            }
            boxSize = checked((long)ReadUInt64(stream));
            headerSize = 16;
        }
        else if (size == 0)
        {
            boxSize = parentEnd - start;
        }

        if (boxSize < headerSize || start + boxSize > parentEnd)
        {
            return false;
        }

        box = new Box(type, start + boxSize);
        return true;
    }

    private static uint ReadUInt32(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[4];
        stream.ReadExactly(buffer);
        return BinaryPrimitives.ReadUInt32BigEndian(buffer);
    }

    private static ulong ReadUInt64(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[8];
        stream.ReadExactly(buffer);
        return BinaryPrimitives.ReadUInt64BigEndian(buffer);
    }

    private readonly record struct Box(string Type, long End);

    private sealed class ParseContext
    {
        public Dictionary<uint, TrackInfo> Tracks { get; } = [];
        public Dictionary<uint, uint> DefaultDurations { get; } = [];
        public Dictionary<uint, ulong> FragmentEnds { get; } = [];

        public TimeSpan? GetVideoDuration()
        {
            var candidates = Tracks.Values.Where(track => track.IsVideo).ToArray();
            if (candidates.Length == 0)
            {
                candidates = Tracks.Values.ToArray();
            }

            double maximumSeconds = 0;
            foreach (var track in candidates.Where(track => track.TimeScale > 0))
            {
                var duration = track.DeclaredDuration;
                if (FragmentEnds.TryGetValue(track.Id, out var fragmentEnd))
                {
                    duration = Math.Max(duration, fragmentEnd);
                }

                maximumSeconds = Math.Max(maximumSeconds, duration / (double)track.TimeScale);
            }

            return maximumSeconds > 0 && double.IsFinite(maximumSeconds)
                ? TimeSpan.FromSeconds(maximumSeconds)
                : null;
        }
    }

    private sealed class TrackInfo
    {
        public uint Id { get; set; }
        public uint TimeScale { get; set; }
        public ulong DeclaredDuration { get; set; }
        public bool IsVideo { get; set; }
    }
}
