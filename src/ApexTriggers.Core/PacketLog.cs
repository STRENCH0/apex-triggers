using System.Text;

namespace ApexTriggers.Core;

/// <summary>Append-only text log of packets and events, rotated to <c>.1</c> when it outgrows its limit.</summary>
public sealed class PacketLog
{
    private readonly string _path;
    private readonly long _maxBytes;
    private readonly object _gate = new();

    public PacketLog(string path, long maxBytes = 2 * 1024 * 1024)
    {
        _path = path;
        _maxBytes = maxBytes;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
    }

    public string FilePath => _path;

    /// <summary>Raised after every line is written; the tester echoes it to the console in verbose mode.</summary>
    public event Action<string>? LineWritten;

    public void Tx(byte[] data) => Write("TX " + Hex(data));
    public void Rx(byte[] data) => Write("RX " + Hex(data));
    public void Info(string message) => Write("-- " + message);

    public void Write(string text)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {text}";
        lock (_gate)
        {
            try
            {
                var file = new FileInfo(_path);
                if (file.Exists && file.Length > _maxBytes)
                    File.Move(_path, _path + ".1", overwrite: true);
                File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (IOException)
            {
                // Logging must never take the program down.
            }
        }
        LineWritten?.Invoke(line);
    }

    /// <summary>Hex up to the last non-zero byte, so 32-byte packets stay readable.</summary>
    public static string Hex(byte[] data)
    {
        var end = data.Length;
        while (end > 6 && data[end - 1] == 0) end--;
        var sb = new StringBuilder(end * 3);
        for (var i = 0; i < end; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(data[i].ToString("x2"));
        }
        return sb.ToString();
    }
}
