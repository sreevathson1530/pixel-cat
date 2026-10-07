using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using Pixel.App.Shell;
using Pixel.Core.Brain;

namespace Pixel.App.Brain;

/// <summary>Starts llama-server hidden, at below-normal priority, inside a kill-on-close job object.</summary>
internal sealed class LlamaLauncher : IServerLauncher
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pixel", "llama-server.log");

    /// <summary>PID of the most recently launched server (to verify who owns its port).</summary>
    public int? LastProcessId { get; private set; }

    public IServerProcess Launch(ServerOptions options)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
        var log = new StreamWriter(LogPath, append: false) { AutoFlush = true };
        log.WriteLine($"{DateTime.Now:O} starting {options.ExePath} {options.Arguments}");
        LastProcessId = null;

        var psi = new ProcessStartInfo(options.ExePath, options.Arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(options.ExePath)!,
        };
        if (options.ApiKey is not null) psi.Environment["LLAMA_API_KEY"] = options.ApiKey; // never on the command line
        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (log) log.WriteLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (log) log.WriteLine(e.Data); };
        p.Exited += (_, _) =>
        {
            lock (log) log.WriteLine($"{DateTime.Now:O} exited with code {SafeExitCode(p)}");
            Log.Info($"llama-server exited ({SafeExitCode(p)})");
        };
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch (InvalidOperationException) { }
        KillOnCloseJob.Assign(p);
        LastProcessId = p.Id;
        Log.Info($"llama-server started (pid {p.Id}): {options.Arguments}");
        return new Handle(p);
    }

    private static string SafeExitCode(Process p)
    {
        try { return p.ExitCode.ToString(); }
        catch (InvalidOperationException) { return "?"; }
    }

    private sealed class Handle(Process p) : IServerProcess
    {
        public bool HasExited
        {
            get
            {
                try { return p.HasExited; }
                catch (InvalidOperationException) { return true; }
            }
        }

        public void Kill()
        {
            try { if (!p.HasExited) p.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        }
    }
}

/// <summary>
/// Ready = GET /health returns 200 (503 while loading) AND the process listening on that port is the
/// llama-server we started — so a program squatting on the port can never receive the user's screen text.
/// </summary>
internal sealed class HealthProbe(HttpClient http, Func<ServerOptions?> current, Func<int?> expectedPid) : IHealthProbe
{
    public async Task<bool> IsReadyAsync(CancellationToken ct)
    {
        if (current() is not { } o || expectedPid() is not { } pid) return false;
        try
        {
            using var r = await http.GetAsync($"http://127.0.0.1:{o.Port}/health", ct);
            if (!r.IsSuccessStatusCode) return false;
            var owner = TcpOwner.ListenerPid(o.Port);
            if (owner == pid) return true;
            Log.Error($"port {o.Port} is owned by pid {owner}, not llama-server {pid}; refusing to use it");
            return false;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }
}

/// <summary>Which process owns a listening TCP port on this machine (IPv4).</summary>
internal static class TcpOwner
{
    public static int? ListenerPid(int port)
    {
        const int AF_INET = 2, TCP_TABLE_OWNER_PID_LISTENER = 3;
        var size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_LISTENER, 0);
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buffer, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_LISTENER, 0) != 0) return null;
            var count = Marshal.ReadInt32(buffer);
            var rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();
            for (var i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(buffer + 4 + i * rowSize);
                var rowPort = (int)(((row.localPort & 0xFF) << 8) | ((row.localPort >> 8) & 0xFF));
                if (rowPort == port) return (int)row.owningPid;
            }
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCPROW_OWNER_PID
    {
        public uint state, localAddr, localPort, remoteAddr, remotePort, owningPid;
    }

    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int af, int tableClass, uint reserved);
}

/// <summary>Job object so llama-server (and its 9 GB) dies with Pixel, even if Pixel crashes.</summary>
internal static class KillOnCloseJob
{
    private static readonly IntPtr Job = Create();

    public static void Assign(Process p)
    {
        if (Job != IntPtr.Zero) AssignProcessToJobObject(Job, p.Handle);
    }

    private static IntPtr Create()
    {
        var job = CreateJobObject(IntPtr.Zero, null);
        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        info.BasicLimitInformation.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        var size = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, ptr, false);
            SetInformationJobObject(job, 9 /* ExtendedLimitInformation */, ptr, (uint)size);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
        return job;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);

    [DllImport("kernel32.dll")]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);

    [DllImport("kernel32.dll")]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
}

/// <summary>Free physical memory and free commit (what Windows can still hand out).</summary>
internal static class MemoryInfo
{
    public static (long AvailablePhys, long AvailableCommit) Read()
    {
        var s = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref s) ? ((long)s.ullAvailPhys, (long)s.ullAvailPageFile) : (long.MaxValue, long.MaxValue);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);
}
