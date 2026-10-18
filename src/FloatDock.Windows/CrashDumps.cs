using System.Runtime.InteropServices;
using FloatDock.Core;
using Microsoft.Win32.SafeHandles;

namespace FloatDock.Windows;

/// <summary>
/// A minidump of this process (threads, modules, stacks and data segments, no heap), written next to the log when the
/// process is about to die from an unhandled exception. It stays on this machine; nothing is sent anywhere.
/// </summary>
public static class CrashDumps
{
    private const int Keep = 5;
    private const uint DataSegs = 0x1, HandleData = 0x4, UnloadedModules = 0x20, ThreadInfo = 0x1000;

    public static string DefaultDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FloatDock", "crashes");

    /// <summary>Returns the dump's path, or null when it could not be written. Never throws.</summary>
    public static string? Write(string? directory = null)
    {
        try
        {
            directory ??= DefaultDirectory;
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"floatdock-{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.dmp");
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var process = System.Diagnostics.Process.GetCurrentProcess();
                var written = MiniDumpWriteDump(process.Handle, (uint)process.Id, stream.SafeFileHandle, DataSegs | HandleData | UnloadedModules | ThreadInfo, 0, 0, 0);
                if (!written) return null;
            }
            Prune(directory);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or DllNotFoundException or EntryPointNotFoundException)
        {
            LocalLog.Write("Warning", "crash-dump-failed", ex);
            return null;
        }
    }

    private static void Prune(string directory)
    {
        foreach (var old in new DirectoryInfo(directory).GetFiles("floatdock-*.dmp").OrderByDescending(file => file.LastWriteTimeUtc).Skip(Keep))
        {
            try { old.Delete(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    [DllImport("dbghelp.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MiniDumpWriteDump(nint process, uint processId, SafeFileHandle file, uint dumpType, nint exception, nint userStream, nint callback);
}
