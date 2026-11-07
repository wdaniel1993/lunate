using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding;

/// <summary>
/// The POSIX half of <see cref="ChildProcess"/>: <c>posix_spawn</c> with
/// <c>POSIX_SPAWN_SETSID</c> so the child starts in a new session with no controlling terminal.
/// Its stdin is <c>/dev/null</c> (or a pipe when a line handler answers it) and its stdout/stderr
/// are captured pipes, so a build tool inside the child cannot open <c>/dev/tty</c> and undo the
/// session's raw mode.
/// </summary>
internal sealed class UnixChildProcess : ChildProcess
{
    private const int PosixSpawnSetSid = 0x0400;
    private const int OpenReadOnly = 0;
    private const int WaitNoHang = 1;
    private static readonly TimeSpan ReapTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan WaitPoll = TimeSpan.FromMilliseconds(10);
    private static readonly System.Text.UTF8Encoding Utf8 = new(
        encoderShouldEmitUTF8Identifier: false
    );

    private readonly int _pid;
    private readonly StreamReader _stdout;
    private readonly StreamReader _stderr;
    private readonly StreamWriter? _stdin;
    private readonly AnonymousPipeServerStream? _stdoutPipe;
    private readonly AnonymousPipeServerStream? _stderrPipe;
    private readonly AnonymousPipeServerStream? _stdinPipe;
    private bool _reaped;
    private bool _disposed;

    private UnixChildProcess(
        int pid,
        StreamReader stdout,
        StreamReader stderr,
        StreamWriter? stdin,
        AnonymousPipeServerStream? stdoutPipe,
        AnonymousPipeServerStream? stderrPipe,
        AnonymousPipeServerStream? stdinPipe
    )
    {
        _pid = pid;
        _stdout = stdout;
        _stderr = stderr;
        _stdin = stdin;
        _stdoutPipe = stdoutPipe;
        _stderrPipe = stderrPipe;
        _stdinPipe = stdinPipe;
    }

    public override int Id => _pid;

    public override StreamReader StandardOutput => _stdout;

    public override StreamReader StandardError => _stderr;

    public override StreamWriter? StandardInput => _stdin;

    public static UnixChildProcess Spawn(ProcessRunRequest request)
    {
        AnonymousPipeServerStream? stdoutPipe = null;
        AnonymousPipeServerStream? stderrPipe = null;
        AnonymousPipeServerStream? stdinPipe = null;
        try
        {
            stdoutPipe = new AnonymousPipeServerStream(
                PipeDirection.In,
                HandleInheritability.Inheritable
            );
            stderrPipe = new AnonymousPipeServerStream(
                PipeDirection.In,
                HandleInheritability.Inheritable
            );
            stdinPipe = request.LineHandler is null
                ? null
                : new AnonymousPipeServerStream(
                    PipeDirection.Out,
                    HandleInheritability.Inheritable
                );

            int stdoutWrite = stdoutPipe.ClientSafePipeHandle.DangerousGetHandle().ToInt32();
            int stderrWrite = stderrPipe.ClientSafePipeHandle.DangerousGetHandle().ToInt32();
            int stdoutRead = stdoutPipe.SafePipeHandle.DangerousGetHandle().ToInt32();
            int stderrRead = stderrPipe.SafePipeHandle.DangerousGetHandle().ToInt32();

            IntPtr attributes;
            IntPtr actions;
            ThrowIfNegative(posix_spawnattr_init(out attributes), "posix_spawnattr_init");
            try
            {
                ThrowIfNegative(
                    posix_spawnattr_setflags(ref attributes, PosixSpawnSetSid),
                    "posix_spawnattr_setflags"
                );
                ThrowIfNegative(
                    posix_spawn_file_actions_init(out actions),
                    "posix_spawn_file_actions_init"
                );
                try
                {
                    if (stdinPipe is null)
                    {
                        ThrowIfNegative(
                            posix_spawn_file_actions_addopen(
                                ref actions,
                                0,
                                "/dev/null",
                                OpenReadOnly,
                                0
                            ),
                            "posix_spawn_file_actions_addopen(stdin)"
                        );
                    }
                    else
                    {
                        int stdinRead = stdinPipe
                            .ClientSafePipeHandle.DangerousGetHandle()
                            .ToInt32();
                        ThrowIfNegative(
                            posix_spawn_file_actions_adddup2(ref actions, stdinRead, 0),
                            "posix_spawn_file_actions_adddup2(stdin)"
                        );
                        ThrowIfNegative(
                            posix_spawn_file_actions_addclose(ref actions, stdinRead),
                            "posix_spawn_file_actions_addclose(stdin)"
                        );
                    }

                    ThrowIfNegative(
                        posix_spawn_file_actions_adddup2(ref actions, stdoutWrite, 1),
                        "posix_spawn_file_actions_adddup2(stdout)"
                    );
                    ThrowIfNegative(
                        posix_spawn_file_actions_addclose(ref actions, stdoutWrite),
                        "posix_spawn_file_actions_addclose(stdout)"
                    );
                    ThrowIfNegative(
                        posix_spawn_file_actions_adddup2(ref actions, stderrWrite, 2),
                        "posix_spawn_file_actions_adddup2(stderr)"
                    );
                    ThrowIfNegative(
                        posix_spawn_file_actions_addclose(ref actions, stderrWrite),
                        "posix_spawn_file_actions_addclose(stderr)"
                    );
                    ThrowIfNegative(
                        posix_spawn_file_actions_addclose(ref actions, stdoutRead),
                        "posix_spawn_file_actions_addclose(stdout-read)"
                    );
                    ThrowIfNegative(
                        posix_spawn_file_actions_addclose(ref actions, stderrRead),
                        "posix_spawn_file_actions_addclose(stderr-read)"
                    );
                    ThrowIfNegative(
                        posix_spawn_file_actions_addchdir_np(ref actions, request.WorkingDirectory),
                        "posix_spawn_file_actions_addchdir_np"
                    );

                    string[] argv = [request.FileName, .. request.Arguments];
                    string[] envp = BuildEnvp(request);
                    string executable = ResolveExecutable(request.FileName);

                    // argv/envp are built as raw NUL-terminated UTF-8 pointer arrays. Passing a
                    // managed string[] lets the runtime marshal it, and a value in the inherited
                    // environment (long tokens, unicode) can make macOS posix_spawn fail with
                    // EFAULT; the explicit pointers are stable.
                    IntPtr argvPtr = AllocArgv(argv);
                    IntPtr envpPtr = AllocArgv(envp);
                    try
                    {
                        int spawn = posix_spawn(
                            out int pid,
                            executable,
                            ref actions,
                            ref attributes,
                            argvPtr,
                            envpPtr
                        );
                        if (spawn != 0)
                        {
                            throw new InvalidOperationException(
                                $"could not start {request.FileName}: posix_spawn failed with error {spawn}"
                            );
                        }

                        var stdout = new StreamReader(stdoutPipe, Utf8);
                        var stderr = new StreamReader(stderrPipe, Utf8);
                        StreamWriter? stdin = stdinPipe is null
                            ? null
                            : new StreamWriter(stdinPipe, Utf8) { AutoFlush = true };

                        var child = new UnixChildProcess(
                            pid,
                            stdout,
                            stderr,
                            stdin,
                            stdoutPipe,
                            stderrPipe,
                            stdinPipe
                        );

                        // The parent's copies of the child-side ends are closed so EOF propagates
                        // when the child exits; the read/write ends stay open through the streams.
                        stdoutPipe.ClientSafePipeHandle.Close();
                        stderrPipe.ClientSafePipeHandle.Close();
                        stdinPipe?.ClientSafePipeHandle.Close();

                        // Ownership of the pipes passes to the child; the local references are
                        // cleared so a later failure does not double-dispose them.
                        stdoutPipe = null;
                        stderrPipe = null;
                        stdinPipe = null;
                        return child;
                    }
                    finally
                    {
                        FreeArgv(argvPtr);
                        FreeArgv(envpPtr);
                    }
                }
                finally
                {
                    posix_spawn_file_actions_destroy(ref actions);
                }
            }
            finally
            {
                posix_spawnattr_destroy(ref attributes);
            }
        }
        finally
        {
            stdoutPipe?.Dispose();
            stderrPipe?.Dispose();
            stdinPipe?.Dispose();
        }
    }

    public override async Task<int> WaitForExitAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int reaped = waitpid(_pid, out int status, WaitNoHang);
            if (reaped == _pid)
            {
                _reaped = true;
                return status >> 8;
            }

            if (reaped < 0)
            {
                _reaped = true;
                return -1;
            }

            await Task.Delay(WaitPoll, cancellationToken).ConfigureAwait(false);
        }
    }

    public override async Task KillTreeAsync()
    {
        if (_reaped)
        {
            return;
        }

        ProcessRunner.KillPosixTree(_pid);
        var deadline = DateTime.UtcNow + ReapTimeout;
        while (DateTime.UtcNow < deadline)
        {
            int reaped = waitpid(_pid, out _, WaitNoHang);
            if (reaped == _pid || reaped < 0)
            {
                _reaped = true;
                return;
            }

            await Task.Delay(WaitPoll).ConfigureAwait(false);
        }
    }

    public override void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _stdin?.Dispose();
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // The child closed its stdin; the final flush lands on a broken pipe.
        }

        _stdout.Dispose();
        _stderr.Dispose();
        _stdoutPipe?.Dispose();
        _stderrPipe?.Dispose();
        _stdinPipe?.Dispose();
    }

    private static string[] BuildEnvp(ProcessRunRequest request) =>
        BuildEnvironment(request).Select(entry => $"{entry.Key}={entry.Value}").ToArray();

    /// <summary>Allocates a NUL-terminated array of UTF-8 string pointers, plus a trailing NULL.</summary>
    private static IntPtr AllocArgv(IReadOnlyList<string> values)
    {
        IntPtr array = Marshal.AllocCoTaskMem((values.Count + 1) * IntPtr.Size);
        for (var index = 0; index < values.Count; index++)
        {
            Marshal.WriteIntPtr(
                array,
                index * IntPtr.Size,
                Marshal.StringToCoTaskMemUTF8(values[index])
            );
        }

        Marshal.WriteIntPtr(array, values.Count * IntPtr.Size, IntPtr.Zero);
        return array;
    }

    private static void FreeArgv(IntPtr array)
    {
        for (var index = 0; ; index++)
        {
            IntPtr entry = Marshal.ReadIntPtr(array, index * IntPtr.Size);
            if (entry == IntPtr.Zero)
            {
                break;
            }

            Marshal.FreeCoTaskMem(entry);
        }

        Marshal.FreeCoTaskMem(array);
    }

    /// <summary>
    /// Resolves a bare executable name against the parent's PATH, matching how
    /// <see cref="System.Diagnostics.Process.Start(string)"/> finds the file; <c>posix_spawn</c>
    /// does not search PATH itself. A name with a directory separator is used as given.
    /// </summary>
    private static string ResolveExecutable(string fileName)
    {
        if (fileName.Contains('/'))
        {
            return fileName;
        }

        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return fileName;
        }

        foreach (string directory in path.Split(Path.PathSeparator))
        {
            string candidate = Path.Combine(directory.Length == 0 ? "." : directory, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return fileName;
    }

    private static void ThrowIfNegative(int result, string what)
    {
        if (result != 0)
        {
            throw new InvalidOperationException(
                $"{what} failed with error {result} ({Marshal.GetLastPInvokeError()})"
            );
        }
    }

    // posix_spawnattr_t and posix_spawn_file_actions_t are opaque `void*` handles; every entry
    // point takes the address of the handle (`void**`), so the handles pass by reference.
    [DllImport("libc", SetLastError = true)]
    private static extern int posix_spawn(
        out int pid,
        string path,
        ref IntPtr fileActions,
        ref IntPtr attributes,
        IntPtr argv,
        IntPtr envp
    );

    [DllImport("libc", SetLastError = true)]
    private static extern int posix_spawnattr_init(out IntPtr attributes);

    [DllImport("libc", SetLastError = true)]
    private static extern int posix_spawnattr_setflags(ref IntPtr attributes, short flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int posix_spawnattr_destroy(ref IntPtr attributes);

    [DllImport("libc", SetLastError = true)]
    private static extern int posix_spawn_file_actions_init(out IntPtr actions);

    [DllImport("libc", SetLastError = true)]
    private static extern int posix_spawn_file_actions_destroy(ref IntPtr actions);

    [DllImport("libc", SetLastError = true)]
    private static extern int posix_spawn_file_actions_addopen(
        ref IntPtr actions,
        int fd,
        string path,
        int oflag,
        uint mode
    );

    [DllImport("libc", SetLastError = true)]
    private static extern int posix_spawn_file_actions_adddup2(
        ref IntPtr actions,
        int src,
        int dst
    );

    [DllImport("libc", SetLastError = true)]
    private static extern int posix_spawn_file_actions_addclose(ref IntPtr actions, int fd);

    [DllImport("libc", SetLastError = true)]
    private static extern int posix_spawn_file_actions_addchdir_np(ref IntPtr actions, string path);

    [DllImport("libc", SetLastError = true)]
    private static extern int waitpid(int pid, out int status, int options);
}
