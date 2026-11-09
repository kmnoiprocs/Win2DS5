using HidSharp;
using Win2DS5.Core;

namespace Win2DS5.DualSense;

public sealed class DualSenseConnection : IDisposable
{
    private HidStream? _stream;
    private byte[] _buffer = [];
    private int _outputLength;
    private byte _sequence;
    private long _lastFullRequest;

    private DualSenseConnection()
    {
    }

    public string DevicePath { get; private set; } = "";
    public bool IsBluetooth { get; private set; }
    public bool IsEdge { get; private set; }
    public bool Disconnected { get; private set; }
    public bool IsOpen => _stream != null && !Disconnected;

    public static DualSenseConnection? TryOpen()
    {
        IEnumerable<HidDevice> devices;
        try
        {
            devices = DeviceList.Local.GetHidDevices();
        }
        catch
        {
            return null;
        }

        var candidates = new List<(HidDevice Device, int Max, bool Bluetooth)>();
        foreach (var device in devices)
        {
            if (device.VendorID != DualSenseIds.Vendor)
                continue;
            if (device.ProductID != DualSenseIds.DualSense && device.ProductID != DualSenseIds.Edge)
                continue;
            int max;
            try
            {
                max = device.GetMaxInputReportLength();
            }
            catch
            {
                continue;
            }

            if (max < 16)
                continue;
            bool bluetooth = IsBluetoothPath(device.DevicePath) || max >= 78;
            candidates.Add((device, max, bluetooth));
        }

        foreach (var candidate in candidates.OrderBy(item => Math.Abs(item.Max - (item.Bluetooth ? 78 : 64))))
        {
            var config = new OpenConfiguration();
            config.SetOption(OpenOption.Exclusive, false);
            if (!candidate.Device.TryOpen(config, out var stream))
                continue;

            stream.ReadTimeout = 80;
            stream.WriteTimeout = 80;
            int outputLength = 64;
            try
            {
                outputLength = candidate.Device.GetMaxOutputReportLength();
            }
            catch
            {
                outputLength = candidate.Bluetooth ? 78 : 63;
            }

            var connection = new DualSenseConnection
            {
                _stream = stream,
                _buffer = new byte[Math.Max(candidate.Max, 80)],
                _outputLength = Math.Max(outputLength, candidate.Bluetooth ? 78 : 48),
                DevicePath = candidate.Device.DevicePath ?? "",
                IsEdge = candidate.Device.ProductID == DualSenseIds.Edge,
                IsBluetooth = candidate.Bluetooth,
            };

            if (connection.TryRead(out _))
            {
                if (connection.IsBluetooth)
                    connection.RequestFullReport();
                return connection;
            }

            connection.Dispose();
        }

        return null;
    }

    public bool TryRead(out ControllerFrame frame)
    {
        frame = default;
        if (_stream == null || Disconnected)
            return false;

        try
        {
            int read = _stream.Read(_buffer, 0, _buffer.Length);
            if (read <= 0)
                return false;
            if (!DualSenseParser.TryParse(_buffer.AsSpan(0, read), IsEdge, out frame))
                return false;
            if (IsBluetooth)
                frame.Bluetooth = true;
            if (frame.Bluetooth && reportLooksShort(read) && Environment.TickCount64 - _lastFullRequest > 1000)
                RequestFullReport();
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (ObjectDisposedException)
        {
            Disconnected = true;
            return false;
        }
        catch (IOException)
        {
            Disconnected = true;
            return false;
        }
        catch (Exception ex) when (ex.GetType().Name.Contains("Timeout", StringComparison.Ordinal))
        {
            return false;
        }
    }

    public void SetDesktopFeedback(byte red, byte green, byte blue, byte rightMotor, byte leftMotor)
    {
        var report = IsBluetooth
            ? DualSenseOutput.BuildBluetooth(ref _sequence, rightMotor, leftMotor, red, green, blue, true, !_setupSent())
            : DualSenseOutput.BuildUsb(rightMotor, leftMotor, red, green, blue, true, !_setupSent());
        _setup = true;
        TryWrite(report);
    }

    private bool _setup;

    private bool _setupSent() => _setup;

    public void RequestFullReport()
    {
        _lastFullRequest = Environment.TickCount64;
        if (!IsBluetooth || _stream == null)
            return;

        var report = DualSenseOutput.BuildBluetooth(ref _sequence, 0, 0, 0, 0, 0, false, false);
        TryWrite(report);
        try
        {
            var feature = new byte[41];
            feature[0] = 0x05;
            _stream.GetFeature(feature);
        }
        catch
        {
            // Calibration is optional. The output report is what switches Bluetooth into the full report.
        }
    }

    public void Dispose()
    {
        try
        {
            _stream?.Dispose();
        }
        catch
        {
            // The device may already be gone.
        }

        _stream = null;
    }

    private void TryWrite(byte[] report)
    {
        if (_stream == null)
            return;
        try
        {
            if (_outputLength > report.Length)
            {
                var padded = new byte[_outputLength];
                report.CopyTo(padded, 0);
                _stream.Write(padded);
                return;
            }

            if (_outputLength > 0 && _outputLength < report.Length)
            {
                var trimmed = new byte[_outputLength];
                Array.Copy(report, trimmed, _outputLength);
                _stream.Write(trimmed);
                return;
            }

            _stream.Write(report);
        }
        catch (IOException)
        {
            Disconnected = true;
        }
        catch (ObjectDisposedException)
        {
            Disconnected = true;
        }
        catch
        {
            // A rejected output report should not stop reading.
        }
    }

    private static bool reportLooksShort(int length) => length < 48;

    private static bool IsBluetoothPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return false;
        return path.Contains("BTH", StringComparison.OrdinalIgnoreCase)
            || path.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase)
            || path.Contains("00001124", StringComparison.OrdinalIgnoreCase);
    }
}
