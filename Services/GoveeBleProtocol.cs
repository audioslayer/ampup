namespace AmpUp.Services;

/// <summary>
/// Govee Bluetooth LE packet format and per-model layouts, for lights that never got Govee's
/// LAN API (e.g. the DreamView G1S Pro, H604C). Pure functions: no Bluetooth calls here.
///
/// Packets are 20 bytes: the command, zero padding, and a final byte that XORs the first 19.
/// Verified on an H604C:
///   33 01 01|00                      power on / off
///   33 04 &lt;0-100&gt;                    brightness
///   33 05 15 01 R G B 00×5 &lt;mask LE&gt;   color for every segment whose bit is set in the mask
///   33 05 00 &lt;5 bytes&gt;               camera (Video) mode; the light syncs itself from its camera
///   AA 01 / AA 04 / AA 05 01         keep-alive + power / brightness / mode queries (answered on notify)
/// </summary>
public static class GoveeBleProtocol
{
    public static readonly Guid ServiceId = Guid.Parse("00010203-0405-0607-0809-0a0b0c0d1910");
    public static readonly Guid WriteCharId = Guid.Parse("00010203-0405-0607-0809-0a0b0c0d2b11");
    public static readonly Guid NotifyCharId = Guid.Parse("00010203-0405-0607-0809-0a0b0c0d2b10");

    /// <summary>Camera-mode settings an H604C reported before AmpUp touched it.</summary>
    public static readonly byte[] DefaultCameraMode = { 0x00, 0x00, 0x32, 0x06, 0x01, 0x32 };

    public static byte[] Packet(params byte[] head)
    {
        var p = new byte[20];
        Array.Copy(head, p, Math.Min(head.Length, 19));
        byte x = 0;
        for (int i = 0; i < 19; i++) x ^= p[i];
        p[19] = x;
        return p;
    }

    public static byte[] KeepAlive() => Packet(0xAA, 0x01);
    public static byte[] QueryMode() => Packet(0xAA, 0x05, 0x01);
    public static byte[] Power(bool on) => Packet(0x33, 0x01, (byte)(on ? 1 : 0));
    public static byte[] Brightness(int percent) => Packet(0x33, 0x04, (byte)Math.Clamp(percent, 1, 100));

    public static byte[] SegmentColor(byte r, byte g, byte b, uint mask) =>
        Packet(0x33, 0x05, 0x15, 0x01, r, g, b, 0, 0, 0, 0, 0,
            (byte)mask, (byte)(mask >> 8), (byte)(mask >> 16), (byte)(mask >> 24));

    /// <summary>33 05 + the mode bytes a mode query returned (byte 0 = mode 00, then its settings).</summary>
    public static byte[] CameraMode(byte[]? modeBytes)
    {
        var m = modeBytes is { Length: >= 2 } ? modeBytes : DefaultCameraMode;
        var head = new byte[2 + m.Length];
        head[0] = 0x33; head[1] = 0x05;
        Array.Copy(m, 0, head, 2, m.Length);
        return Packet(head);
    }

    /// <summary>
    /// A mode reply "AA 05 00 &lt;settings&gt;" means the light is in camera mode; returns those
    /// mode bytes (mode + settings) so AmpUp can switch back to exactly the user's camera setup.
    /// </summary>
    public static bool TryParseCameraMode(byte[] reply, out byte[] modeBytes)
    {
        modeBytes = Array.Empty<byte>();
        if (reply.Length < 8 || reply[0] != 0xAA || reply[1] != 0x05 || reply[2] != 0x00) return false;
        bool anySetting = false;
        for (int i = 3; i < 8; i++) anySetting |= reply[i] != 0;
        if (!anySetting) return false;
        modeBytes = reply[2..8];
        return true;
    }

    public static string ToHex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", " ");

    public static byte[]? FromHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        try { return hex.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(h => Convert.ToByte(h, 16)).ToArray(); }
        catch { return null; }
    }

    /// <summary>
    /// Govee device ids are 8 bytes: 2 bytes then the light's 6-byte Bluetooth address
    /// (1E:51:D5:32:37:36:74:6C advertises as D5:32:37:36:74:6C).
    /// </summary>
    public static bool TryGetBleAddress(string? deviceId, out ulong address)
    {
        address = 0;
        var parts = (deviceId ?? "").Split(':');
        if (parts.Length != 8) return false;
        try
        {
            for (int i = 2; i < 8; i++) address = (address << 8) | Convert.ToByte(parts[i], 16);
            return address != 0;
        }
        catch { return false; }
    }

    // ── Models ────────────────────────────────────────────────────────────

    /// <summary>A group of segments that gets one color, at a left-to-right room position (0-1).</summary>
    public sealed record Zone(string Name, uint Mask, float X);

    public sealed record Profile(string Sku, Zone[] Zones);

    private static uint Bits(params int[] segments) => segments.Aggregate(0u, (m, s) => m | (1u << s));

    /// <summary>
    /// Models AmpUp can drive over Bluetooth. H604C layout (mapped on a real kit): strip 0-14 runs
    /// all the way round the monitor (0 = top center, down the right side, 7 = bottom center,
    /// up the left side); left bar 15 (bottom) to 18 (top); right bar 19 (bottom) to 23 (top).
    /// One packet colors one zone, and the light takes ~25 packets/s, so six zones keep room
    /// effects moving at a few updates per zone per second.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, Profile> Profiles = new Dictionary<string, Profile>(StringComparer.OrdinalIgnoreCase)
    {
        ["H604C"] = new Profile("H604C", new[]
        {
            new Zone("Left bar",                 Bits(15, 16, 17, 18),     0.03f),
            new Zone("Strip left side",          Bits(10, 11, 12),         0.20f),
            new Zone("Strip top/bottom left",    Bits(13, 14, 8, 9),       0.38f),
            new Zone("Strip top/bottom right",   Bits(0, 1, 5, 6, 7),      0.62f),
            new Zone("Strip right side",         Bits(2, 3, 4),            0.80f),
            new Zone("Right bar",                Bits(19, 20, 21, 22, 23), 0.97f),
        }),
    };

    public static Profile? GetProfile(string? sku) =>
        sku != null && Profiles.TryGetValue(sku.Trim(), out var p) ? p : null;

    /// <summary>
    /// Color for each zone from a room-effect frame: 15 RGB LEDs laid out left to right
    /// (45 bytes), sampled at the zone's position with linear interpolation.
    /// </summary>
    public static (byte R, byte G, byte B)[] SampleZones(Profile profile, byte[] frame45)
    {
        var result = new (byte, byte, byte)[profile.Zones.Length];
        int leds = frame45.Length / 3;
        if (leds == 0) return result;
        for (int z = 0; z < profile.Zones.Length; z++)
        {
            float pos = Math.Clamp(profile.Zones[z].X, 0f, 1f) * (leds - 1);
            int a = (int)Math.Floor(pos), b = Math.Min(a + 1, leds - 1);
            float t = pos - a;
            byte Lerp(int ch) => (byte)Math.Round(frame45[a * 3 + ch] * (1 - t) + frame45[b * 3 + ch] * t);
            result[z] = (Lerp(0), Lerp(1), Lerp(2));
        }
        return result;
    }
}
