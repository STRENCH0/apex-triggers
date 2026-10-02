using System.Threading.Channels;
using Microsoft.Win32.SafeHandles;

namespace ApexTriggers.Core.Hid;

/// <summary>
/// Shared-mode read/write handle to one HID collection.
///
/// Opened with FILE_SHARE_READ | FILE_SHARE_WRITE so Steam keeps its own handle. Windows gives every
/// handle its own copy of each input report, so a background loop reads continuously: with Steam
/// holding the pad the vendor collection streams ~1000 reports/s, and a reader that only reads when
/// it expects a reply would overflow the driver's ring buffer and lose that reply.
/// </summary>
public sealed class HidConnection : IDisposable
{
    private const int InputBuffers = 512;

    private readonly SafeFileHandle _handle;
    private readonly FileStream _stream;
    private readonly CancellationTokenSource _stop = new();
    private readonly Channel<byte[]> _reports = Channel.CreateBounded<byte[]>(
        new BoundedChannelOptions(4096) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly Task _readLoop;

    public HidDeviceInfo Info { get; }

    /// <summary>Set when the read loop ends on an I/O error, i.e. the device went away.</summary>
    public bool IsBroken { get; private set; }

    private HidConnection(HidDeviceInfo info, SafeFileHandle handle)
    {
        Info = info;
        _handle = handle;
        _stream = new FileStream(handle, FileAccess.ReadWrite, bufferSize: 0, isAsync: true);
        _readLoop = Task.Run(ReadLoopAsync);
    }

    public static HidConnection Open(HidDeviceInfo info)
    {
        var handle = HidNative.CreateFile(info.Path,
            HidNative.GENERIC_READ | HidNative.GENERIC_WRITE,
            HidNative.FILE_SHARE_READ | HidNative.FILE_SHARE_WRITE, IntPtr.Zero,
            HidNative.OPEN_EXISTING, HidNative.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        if (handle.IsInvalid)
            throw new IOException($"Failed to open HID device (Win32 {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}): {info.Path}");
        HidNative.HidD_SetNumInputBuffers(handle, InputBuffers);
        return new HidConnection(info, handle);
    }

    /// <summary>Discard every report received so far.</summary>
    public void Drain()
    {
        while (_reports.Reader.TryRead(out _)) { }
    }

    /// <summary>Next input report, or null on timeout.</summary>
    public async Task<byte[]?> ReadAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            return await _reports.Reader.ReadAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
        catch (ChannelClosedException)
        {
            return null;
        }
    }

    /// <summary>Write one output report. The buffer is padded to the collection's output report length.</summary>
    public async Task WriteAsync(byte[] report, CancellationToken ct = default)
    {
        var length = Math.Max(Info.OutputReportLength, report.Length);
        var buffer = new byte[length];
        report.CopyTo(buffer, 0);
        try
        {
            await _stream.WriteAsync(buffer, ct);
        }
        catch (IOException)
        {
            // Some collections only take output reports over the control pipe.
            if (!HidNative.HidD_SetOutputReport(_handle, buffer, buffer.Length)) throw;
        }
    }

    private async Task ReadLoopAsync()
    {
        var length = Math.Max(Info.InputReportLength, 64);
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var buffer = new byte[length];
                var read = await _stream.ReadAsync(buffer, _stop.Token);
                if (read <= 0) continue;
                _reports.Writer.TryWrite(read == length ? buffer : buffer[..read]);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) when (!_stop.IsCancellationRequested)
        {
            IsBroken = true;
        }
        finally
        {
            _reports.Writer.TryComplete();
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _readLoop.Wait(TimeSpan.FromSeconds(1)); } catch { /* the loop reports through IsBroken */ }
        _stream.Dispose();
        _handle.Dispose();
        _stop.Dispose();
    }
}
