using System.Diagnostics;
using System.Text;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding;

/// <summary>
/// The interactive half of <c>core/process</c>: one long-lived child with line-oriented
/// stdin/stdout/stderr and a tree kill that reuses the run path's children-first walk. The remote
/// extension host consumes it through <see cref="IProcessRunner.StartSessionAsync"/>.
/// </summary>
internal sealed partial class ProcessRunner
{
    public async Task<IProcessSession> StartSessionAsync(
        ProcessSessionRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FileName);
        ArgumentNullException.ThrowIfNull(request.Arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkingDirectory);
        ArgumentNullException.ThrowIfNull(request.Environment);
        cancellationToken.ThrowIfCancellationRequested();

        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // The environment is exactly what the caller passes: no inherit mode exists, so a missing
        // variable is absent, not inherited.
        startInfo.Environment.Clear();
        foreach ((string name, string value) in request.Environment)
        {
            startInfo.Environment[name] = value;
        }

        Process process =
            Process.Start(startInfo)
            ?? throw new InvalidOperationException("the process did not start");
        var session = new SessionProcess(process, request.MaxLineBytes);
        try
        {
            await session.ProbeAsync(cancellationToken).ConfigureAwait(false);
            return session;
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// One started child: bounded line readers over both streams, a flushed stdin writer and the
    /// shared tree kill. Disposal kills a running child, so the caller always owns shutdown.
    /// </summary>
    private sealed class SessionProcess(Process process, int? maxLineBytes) : IProcessSession
    {
        private readonly SemaphoreSlim _stdinGate = new(1, 1);
        private readonly BoundedLineReader _stdout = new(
            process.StandardOutput.BaseStream,
            maxLineBytes
        );
        private readonly BoundedLineReader _stderr = new(
            process.StandardError.BaseStream,
            maxLineBytes
        );
        private bool _disposed;

        /// <summary>
        /// Confirms the child survived the start window; a process that died between
        /// <c>Process.Start</c> and here still yields a usable session whose streams report EOF, so
        /// this only observes the declared cancellation.
        /// </summary>
        public Task ProbeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public bool HasExited
        {
            get
            {
                try
                {
                    return process.HasExited;
                }
                catch (InvalidOperationException)
                {
                    // The process object was disposed (our own DisposeAsync ran): it was killed.
                    return true;
                }
            }
        }

        public int? ExitCode => HasExited ? process.ExitCode : null;

        public ValueTask<string?> ReadOutputLineAsync(CancellationToken cancellationToken) =>
            _stdout.ReadLineAsync(cancellationToken);

        public ValueTask<string?> ReadErrorLineAsync(CancellationToken cancellationToken) =>
            _stderr.ReadLineAsync(cancellationToken);

        public async ValueTask WriteInputLineAsync(string line, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(line);
            ObjectDisposedException.ThrowIf(_disposed, this);
            await _stdinGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await process
                    .StandardInput.WriteAsync(line.AsMemory(), cancellationToken)
                    .ConfigureAwait(false);
                await process
                    .StandardInput.WriteAsync("\n".AsMemory(), cancellationToken)
                    .ConfigureAwait(false);
                await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsPipeClosed(exception))
            {
                throw new InvalidOperationException(
                    "the child's stdin is closed; the process has exited.",
                    exception
                );
            }
            finally
            {
                _stdinGate.Release();
            }
        }

        public async Task<int> WaitForExitAsync(CancellationToken cancellationToken)
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode;
        }

        public async ValueTask KillAsync()
        {
            if (_disposed || HasExited)
            {
                return;
            }

            await KillTreeAsync(process).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (!HasExited)
            {
                await KillTreeAsync(process).ConfigureAwait(false);
            }

            try
            {
                process.StandardInput.Dispose();
            }
            catch (Exception exception) when (IsPipeClosed(exception)) { }

            process.Dispose();
            _stdinGate.Dispose();
        }
    }

    /// <summary>
    /// Reads UTF-8 lines from one stream with an optional byte bound. A line over the bound is
    /// drained to its newline and rejected with <see cref="InvalidDataException"/> — the reader
    /// never grows past the bound, so a hostile child cannot OOM the host. A trailing <c>\r</c> is
    /// stripped; the last line without a newline is returned at EOF.
    /// </summary>
    private sealed class BoundedLineReader(Stream stream, int? maxLineBytes)
    {
        private const int ChunkSize = 8192;

        private readonly byte[] _pending = new byte[ChunkSize];
        private int _start;
        private int _count;

        public async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            var line = new List<byte>();
            while (true)
            {
                int newline = IndexOfNewline(_pending, _start, _count);
                if (newline >= 0)
                {
                    line.AddRange(new ArraySegment<byte>(_pending, _start, newline - _start));
                    Advance(newline + 1);
                    if (maxLineBytes is { } reached && line.Count > reached)
                    {
                        throw new InvalidDataException(
                            $"a line over {reached.ToString(System.Globalization.CultureInfo.InvariantCulture)} bytes arrived on the child's stream; the line was drained and discarded."
                        );
                    }

                    return Decode(line);
                }

                if (_count > 0)
                {
                    line.AddRange(new ArraySegment<byte>(_pending, _start, _count));
                    _start = 0;
                    _count = 0;
                    if (maxLineBytes is { } bound && line.Count > bound)
                    {
                        await DrainToNewlineAsync(cancellationToken).ConfigureAwait(false);
                        throw new InvalidDataException(
                            $"a line over {bound.ToString(System.Globalization.CultureInfo.InvariantCulture)} bytes arrived on the child's stream; the line was drained and discarded."
                        );
                    }
                }

                int read = await stream
                    .ReadAsync(_pending.AsMemory(), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    return line.Count == 0 ? null : Decode(line);
                }

                _start = 0;
                _count = read;
            }
        }

        private void Advance(int next)
        {
            _count -= next - _start;
            _start = next;
        }

        private async ValueTask DrainToNewlineAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                int newline = IndexOfNewline(_pending, _start, _count);
                if (newline >= 0)
                {
                    Advance(newline + 1);
                    return;
                }

                int read = await stream
                    .ReadAsync(_pending.AsMemory(), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    _start = 0;
                    _count = 0;
                    return;
                }

                _start = 0;
                _count = read;
            }
        }

        private static int IndexOfNewline(byte[] buffer, int start, int count)
        {
            for (var index = start; index < start + count; index++)
            {
                if (buffer[index] == (byte)'\n')
                {
                    return index;
                }
            }

            return -1;
        }

        private static string Decode(List<byte> bytes)
        {
            if (bytes.Count > 0 && bytes[^1] == (byte)'\r')
            {
                bytes.RemoveAt(bytes.Count - 1);
            }

            return Encoding.UTF8.GetString([.. bytes]);
        }
    }
}
