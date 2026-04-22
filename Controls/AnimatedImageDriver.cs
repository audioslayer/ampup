using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace AmpUp.Controls;

/// <summary>
/// Drives per-frame Source updates on WPF Image controls that display
/// animated GIFs. One shared DispatcherTimer serves every registered
/// Image, so we don't spin up one timer per LCD preview tile.
///
/// Usage: call Register(image, frames, delays) after setting Image.Source
/// to the first frame. Register again with a different signature to swap
/// animations; Unregister to stop.
/// </summary>
internal static class AnimatedImageDriver
{
    private sealed class Entry
    {
        public required BitmapSource[] Frames;
        public required int[] FrameDelaysMs;
        public int Index;
        public DateTime NextAtUtc;
        public string Signature = ""; // frame identity (e.g. ImagePath + size)
    }

    private static readonly Dictionary<Image, Entry> s_entries = new();
    private static DispatcherTimer? s_timer;

    public static void Register(Image target, StreamControllerEditorAnimation anim, string signature)
    {
        if (target == null || anim == null || anim.Frames.Length == 0) return;

        // If re-registering the same signature, leave the existing timer
        // state alone so the animation doesn't jerk back to frame 0 on
        // every config-refresh pass.
        if (s_entries.TryGetValue(target, out var existing) && existing.Signature == signature)
            return;

        int firstDelay = Math.Max(40, anim.FrameDelaysMs.Length > 0 ? anim.FrameDelaysMs[0] : 100);
        s_entries[target] = new Entry
        {
            Frames = anim.Frames,
            FrameDelaysMs = anim.FrameDelaysMs,
            Index = 0,
            NextAtUtc = DateTime.UtcNow.AddMilliseconds(firstDelay),
            Signature = signature,
        };
        target.Source = anim.Frames[0];
        target.Unloaded += OnTargetUnloaded;
        EnsureTimer();
    }

    public static void Unregister(Image target)
    {
        if (target == null) return;
        if (s_entries.Remove(target))
        {
            target.Unloaded -= OnTargetUnloaded;
        }
        if (s_entries.Count == 0)
        {
            s_timer?.Stop();
            s_timer = null;
        }
    }

    private static void OnTargetUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is Image img) Unregister(img);
    }

    private static void EnsureTimer()
    {
        if (s_timer != null) return;
        s_timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            // 30 ms tick is enough for the typical 60–100 ms GIF frame
            // delays while keeping CPU idle draw cost tiny.
            Interval = TimeSpan.FromMilliseconds(30),
        };
        s_timer.Tick += OnTick;
        s_timer.Start();
    }

    private static void OnTick(object? sender, EventArgs e)
    {
        if (s_entries.Count == 0) return;
        var now = DateTime.UtcNow;

        // Copy keys — an Unregister during iteration is legal because the
        // Image's Unloaded handler may fire synchronously.
        var targets = new Image[s_entries.Count];
        int i = 0;
        foreach (var t in s_entries.Keys) targets[i++] = t;

        foreach (var target in targets)
        {
            if (!s_entries.TryGetValue(target, out var entry)) continue;
            if (now < entry.NextAtUtc) continue;

            do
            {
                entry.Index = (entry.Index + 1) % entry.Frames.Length;
                int delay = Math.Max(40,
                    entry.FrameDelaysMs.Length > 0
                        ? entry.FrameDelaysMs[Math.Clamp(entry.Index, 0, entry.FrameDelaysMs.Length - 1)]
                        : 100);
                entry.NextAtUtc = entry.NextAtUtc.AddMilliseconds(delay);
            }
            while (now >= entry.NextAtUtc);

            target.Source = entry.Frames[entry.Index];
        }
    }
}
