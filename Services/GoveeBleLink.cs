using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace AmpUp.Services;

/// <summary>
/// One Bluetooth LE connection to a Govee light. Connects (and reconnects) on its own worker,
/// paces writes, and sends a keep-alive when idle so the light doesn't drop the link.
///
/// Sends are keyed: a newer packet for the same key replaces one still waiting ("latest wins"),
/// so a fast room effect never builds a backlog. Priority sends (power, brightness, mode) jump
/// ahead of color updates.
/// </summary>
internal sealed class GoveeBleLink : IDisposable
{
    // An H604C answered reliably at 30 packets/s and stopped answering queries at 45+/s.
    private const int SendIntervalMs = 40;
    private const int KeepAliveMs = 2000;
    private const int ReconnectDelayMs = 5000;

    private readonly ulong _address;
    private readonly string _name;
    private readonly object _gate = new();
    private readonly Dictionary<string, byte[]> _pending = new();
    private readonly LinkedList<string> _order = new();
    private readonly CancellationTokenSource _cts = new();
    private BluetoothLEDevice? _device;
    private GattDeviceService? _service;
    private GattCharacteristic? _write;
    private GattCharacteristic? _notify;
    private volatile bool _connected;
    private string? _lastLoggedProblem;

    public bool IsConnected => _connected;
    public string Name => _name;

    /// <summary>Raised on the link's worker after each (re)connect.</summary>
    public event Action? Connected;

    /// <summary>Notification packets from the light (query replies, ACKs).</summary>
    public event Action<byte[]>? Notification;

    public GoveeBleLink(ulong address, string name)
    {
        _address = address;
        _name = name;
        _ = Task.Run(() => RunAsync(_cts.Token));
    }

    public void Send(string key, byte[] packet, bool priority = false)
    {
        lock (_gate)
        {
            bool waiting = _pending.ContainsKey(key);
            _pending[key] = packet;
            if (waiting && !priority) return;
            if (waiting) _order.Remove(key);
            if (priority) _order.AddFirst(key); else _order.AddLast(key);
        }
    }

    /// <summary>Drops queued color updates (e.g. when switching to camera mode).</summary>
    public void DropPending(Func<string, bool> match)
    {
        lock (_gate)
        {
            foreach (var key in _pending.Keys.Where(match).ToList())
            {
                _pending.Remove(key);
                _order.Remove(key);
            }
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        long lastWrite = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!_connected)
                {
                    if (!await ConnectAsync())
                    {
                        await Task.Delay(ReconnectDelayMs, ct);
                        continue;
                    }
                    Connected?.Invoke();
                }

                byte[]? next = null;
                lock (_gate)
                {
                    if (_order.First is { } first)
                    {
                        _order.RemoveFirst();
                        _pending.Remove(first.Value, out next);
                    }
                }
                if (next == null && Environment.TickCount64 - lastWrite >= KeepAliveMs)
                    next = GoveeBleProtocol.KeepAlive();

                if (next != null)
                {
                    if (!await WriteAsync(next))
                    {
                        Drop("write failed");
                        continue;
                    }
                    lastWrite = Environment.TickCount64;
                }
                await Task.Delay(SendIntervalMs, ct);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                Drop(ex.Message);
                try { await Task.Delay(ReconnectDelayMs, ct); } catch (OperationCanceledException) { return; }
            }
        }
    }

    private async Task<bool> ConnectAsync()
    {
        try
        {
            _device ??= await BluetoothLEDevice.FromBluetoothAddressAsync(_address);
            if (_device == null) { LogProblem("not in range"); return false; }

            GattDeviceService? svc = null;
            for (int attempt = 0; attempt < 3 && svc == null; attempt++)
            {
                var res = await _device.GetGattServicesForUuidAsync(GoveeBleProtocol.ServiceId,
                    attempt == 0 ? BluetoothCacheMode.Cached : BluetoothCacheMode.Uncached);
                svc = res.Services.FirstOrDefault();
                if (svc == null) await Task.Delay(1000);
            }
            if (svc == null) { LogProblem("control service not reachable"); return false; }

            var write = (await svc.GetCharacteristicsForUuidAsync(GoveeBleProtocol.WriteCharId)).Characteristics.FirstOrDefault();
            var notify = (await svc.GetCharacteristicsForUuidAsync(GoveeBleProtocol.NotifyCharId)).Characteristics.FirstOrDefault();
            if (write == null) { LogProblem("write characteristic missing"); return false; }

            if (notify != null)
            {
                notify.ValueChanged += OnValueChanged;
                await notify.WriteClientCharacteristicConfigurationDescriptorAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.Notify);
            }
            _device.ConnectionStatusChanged += OnConnectionStatusChanged;

            _service = svc;
            _write = write;
            _notify = notify;
            _connected = true;
            _lastLoggedProblem = null;
            Logger.Log($"Govee BLE: connected to {_name}");
            return true;
        }
        catch (Exception ex)
        {
            LogProblem(ex.Message);
            return false;
        }
    }

    private async Task<bool> WriteAsync(byte[] packet)
    {
        var w = _write;
        if (w == null) return false;
        var writer = new DataWriter();
        writer.WriteBytes(packet);
        var result = await w.WriteValueWithResultAsync(writer.DetachBuffer(), GattWriteOption.WriteWithoutResponse);
        return result.Status == GattCommunicationStatus.Success;
    }

    private void OnValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        var bytes = new byte[args.CharacteristicValue.Length];
        DataReader.FromBuffer(args.CharacteristicValue).ReadBytes(bytes);
        Notification?.Invoke(bytes);
    }

    private void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
    {
        if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected && _connected)
            Drop("disconnected");
    }

    private void Drop(string reason)
    {
        if (!_connected) return;
        _connected = false;
        Logger.Log($"Govee BLE: lost {_name} ({reason}); reconnecting");
        ReleaseGatt();
    }

    private void ReleaseGatt()
    {
        try { if (_notify != null) _notify.ValueChanged -= OnValueChanged; } catch { }
        try { if (_device != null) _device.ConnectionStatusChanged -= OnConnectionStatusChanged; } catch { }
        try { _service?.Dispose(); } catch { }
        try { _device?.Dispose(); } catch { }
        _service = null; _write = null; _notify = null; _device = null;
    }

    // Retries every few seconds while the light is off or out of range: log each problem once.
    private void LogProblem(string problem)
    {
        if (problem == _lastLoggedProblem) return;
        _lastLoggedProblem = problem;
        Logger.Log($"Govee BLE: can't reach {_name}: {problem} (retrying)");
    }

    public void Dispose()
    {
        _cts.Cancel();
        _connected = false;
        ReleaseGatt();
    }
}
