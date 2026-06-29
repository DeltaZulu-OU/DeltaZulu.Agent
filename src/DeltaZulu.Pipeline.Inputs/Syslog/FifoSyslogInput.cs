using System.ComponentModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using DeltaZulu.Pipeline.Core.Abstractions;
using DeltaZulu.Pipeline.Core.Events;

namespace DeltaZulu.Pipeline.Inputs.Syslog;

/// <summary>
/// Reads syslog-style lines from a Linux FIFO (named pipe) so local processes can
/// push logs by writing to a filesystem path. The FIFO is opened read/write so the
/// agent can start before writers connect.
/// </summary>
public sealed class FifoSyslogInput : ISourceInput
{
    private const uint FifoPermissions = 0x1B6; // 0666 before umask
    private const int StatModeFifoMask = 0x1000;
    private const int StatModeTypeMask = 0xF000;
    private const int NoSuchFileOrDirectory = 2;

    private readonly string _path;
    private readonly LightweightSyslogParser _parser = new();

    public string Name { get; }

    public FifoSyslogInput(string path, string name = "fifo-syslog")
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("FIFO path is required.", nameof(path));
        }

        _path = path;
        Name = name;
    }

    public IObservable<SourceEvent> Open(CancellationToken cancellationToken = default) => Observable.Create<SourceEvent>(observer => {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = Task.Run(async () => {
            try
            {
                EnsureFifo(_path);

                while (!cts.IsCancellationRequested)
                {
                    using var stream = new FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
                    using var reader = new StreamReader(stream);

                    while (!cts.IsCancellationRequested)
                    {
                        var line = await reader.ReadLineAsync(cts.Token).ConfigureAwait(false);
                        if (line is null)
                        {
                            break;
                        }

                        if (line.Length > 0)
                        {
                            observer.OnNext(_parser.Parse(line, Name));
                        }
                    }
                }

                observer.OnCompleted();
            }
            catch (OperationCanceledException)
            {
                observer.OnCompleted();
            }
            catch (Exception ex)
            {
                observer.OnError(ex);
            }
        }, cts.Token);

        return Disposable.Create(() => { cts.Cancel(); cts.Dispose(); });
    });

    public static void EnsureFifo(string path)
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("FIFO input is only supported on Linux.");
        }

        var statResult = stat(path, out var existing);
        if (statResult == 0)
        {
            if (!IsFifo(existing))
            {
                throw new IOException($"'{path}' exists but is not a FIFO named pipe.");
            }

            return;
        }

        var statError = Marshal.GetLastWin32Error();
        if (statError != NoSuchFileOrDirectory)
        {
            throw new IOException($"Failed to stat '{path}': {new Win32Exception(statError).Message}");
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (mkfifo(path, FifoPermissions) != 0)
        {
            throw new IOException($"Failed to create FIFO '{path}': {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
        }
    }

    private static bool IsFifo(StatBuffer buffer) => (buffer.Mode & StatModeTypeMask) == StatModeFifoMask;

    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern int mkfifo(string pathname, uint mode);

    [DllImport("libc", EntryPoint = "stat", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern int stat(string pathname, out StatBuffer buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct StatBuffer
    {
        public ulong Device;
        public ulong Inode;
        public ulong HardLinks;
        public uint Mode;
        public uint UserId;
        public uint GroupId;
        public ulong DeviceId;
        public long Size;
        public long BlockSize;
        public long Blocks;
        public long AccessTime;
        public long AccessTimeNsec;
        public long ModifyTime;
        public long ModifyTimeNsec;
        public long ChangeTime;
        public long ChangeTimeNsec;
        private readonly long _unused1;
        private readonly long _unused2;
        private readonly long _unused3;
    }
}