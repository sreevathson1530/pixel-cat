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

    public IServerProcess Launch(ServerOptions options)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
        var log = new StreamWriter(LogPath, append: false) { AutoFlush = true };
        log.WriteLine($"{DateTime.Now:O} starting {options.ExePath} {options.Arguments}");

        var psi = new ProcessStartInfo(options.ExePath, options.Arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(options.ExePath)!,
        };
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

/// <summary>GET /health returns 200 once the model is loaded (503 while loading).</summary>
internal sealed class HealthProbe(HttpClient http) : IHealthProbe
{
    public async Task<bool> IsReadyAsync(CancellationToken ct)
    {
        try
        {
            using var r = await http.GetAsync("health", ct);
            return r.IsSuccessStatusCode;
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
