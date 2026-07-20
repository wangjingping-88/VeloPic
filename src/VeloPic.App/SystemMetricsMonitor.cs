using System.Runtime.InteropServices;

namespace VeloPic.App;

internal readonly record struct SystemMetricsSnapshot(
    double CpuUsagePercent,
    double MemoryUsagePercent,
    double UsedMemoryGiB,
    double TotalMemoryGiB,
    double? GpuUsagePercent);

internal sealed class SystemMetricsMonitor : IDisposable
{
    private const uint PdhMoreData = 0x800007D2;
    private const uint PdhFmtDouble = 0x00000200;

    private IntPtr _gpuQuery;
    private IntPtr _gpuCounter;
    private bool _disposed;
    private ulong _previousIdle;
    private ulong _previousKernel;
    private ulong _previousUser;

    public SystemMetricsMonitor()
    {
        ReadCpuBaseline();
        InitializeGpuCounter();
    }

    public SystemMetricsSnapshot Read()
    {
        var cpu = ReadCpuUsage();
        var memory = ReadMemoryUsage();
        var gpu = ReadGpuUsage();
        return new SystemMetricsSnapshot(cpu, memory.UsagePercent, memory.UsedGiB, memory.TotalGiB, gpu);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_gpuCounter != IntPtr.Zero)
        {
            PdhRemoveCounter(_gpuCounter);
            _gpuCounter = IntPtr.Zero;
        }

        if (_gpuQuery != IntPtr.Zero)
        {
            PdhCloseQuery(_gpuQuery);
            _gpuQuery = IntPtr.Zero;
        }
    }

    private void ReadCpuBaseline()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return;
        }

        _previousIdle = ToUInt64(idle);
        _previousKernel = ToUInt64(kernel);
        _previousUser = ToUInt64(user);
    }

    private double ReadCpuUsage()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return 0;
        }

        var currentIdle = ToUInt64(idle);
        var currentKernel = ToUInt64(kernel);
        var currentUser = ToUInt64(user);
        var idleDelta = currentIdle - _previousIdle;
        var totalDelta = currentKernel - _previousKernel + currentUser - _previousUser;

        _previousIdle = currentIdle;
        _previousKernel = currentKernel;
        _previousUser = currentUser;

        return totalDelta == 0
            ? 0
            : Math.Clamp((1d - idleDelta / (double)totalDelta) * 100d, 0d, 100d);
    }

    private static (double UsagePercent, double UsedGiB, double TotalGiB) ReadMemoryUsage()
    {
        var status = new MemoryStatusEx
        {
            Length = (uint)Marshal.SizeOf<MemoryStatusEx>()
        };

        if (!GlobalMemoryStatusEx(ref status) || status.TotalPhysicalMemory == 0)
        {
            return (0, 0, 0);
        }

        var totalGiB = status.TotalPhysicalMemory / 1024d / 1024d / 1024d;
        var availableGiB = status.AvailablePhysicalMemory / 1024d / 1024d / 1024d;
        var usedGiB = Math.Max(0, totalGiB - availableGiB);
        var usagePercent = Math.Clamp(usedGiB / totalGiB * 100d, 0d, 100d);
        return (usagePercent, usedGiB, totalGiB);
    }

    private void InitializeGpuCounter()
    {
        if (PdhOpenQuery(null, IntPtr.Zero, out _gpuQuery) != 0)
        {
            _gpuQuery = IntPtr.Zero;
            return;
        }

        const string counterPath = @"\GPU Engine(*)\Utilization Percentage";
        if (PdhAddEnglishCounter(_gpuQuery, counterPath, IntPtr.Zero, out _gpuCounter) != 0 ||
            PdhCollectQueryData(_gpuQuery) != 0)
        {
            Dispose();
        }
    }

    private double? ReadGpuUsage()
    {
        if (_gpuQuery == IntPtr.Zero || _gpuCounter == IntPtr.Zero || PdhCollectQueryData(_gpuQuery) != 0)
        {
            return null;
        }

        uint bufferSize = 0;
        uint itemCount = 0;
        var result = PdhGetFormattedCounterArray(
            _gpuCounter,
            PdhFmtDouble,
            ref bufferSize,
            ref itemCount,
            IntPtr.Zero);

        if (result != PdhMoreData || bufferSize == 0 || itemCount == 0)
        {
            return null;
        }

        var buffer = Marshal.AllocHGlobal((int)bufferSize);
        try
        {
            if (PdhGetFormattedCounterArray(
                    _gpuCounter,
                    PdhFmtDouble,
                    ref bufferSize,
                    ref itemCount,
                    buffer) != 0)
            {
                return null;
            }

            var itemSize = Marshal.SizeOf<PdhCounterValueItem>();
            var maxUsage = 0d;
            for (var index = 0; index < itemCount; index++)
            {
                var item = Marshal.PtrToStructure<PdhCounterValueItem>(IntPtr.Add(buffer, index * itemSize));
                if (item.Value.CStatus == 0)
                {
                    maxUsage = Math.Max(maxUsage, item.Value.DoubleValue);
                }
            }

            return Math.Clamp(maxUsage, 0d, 100d);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static ulong ToUInt64(FileTime time)
    {
        return ((ulong)time.HighDateTime << 32) | time.LowDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysicalMemory;
        public ulong AvailablePhysicalMemory;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PdhCounterValue
    {
        public uint CStatus;
        public double DoubleValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PdhCounterValueItem
    {
        public IntPtr Name;
        public PdhCounterValue Value;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQuery(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounter(IntPtr query, string counterPath, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterArray(
        IntPtr counter,
        uint format,
        ref uint bufferSize,
        ref uint itemCount,
        IntPtr buffer);

    [DllImport("pdh.dll")]
    private static extern uint PdhRemoveCounter(IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);
}
