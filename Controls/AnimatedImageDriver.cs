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
        public required WeakReference<Image> Target;
        public required StreamControllerEditorAnimation Animation;
        public int Index;
        public DateTime NextAtUtc;
        public string Signature = ""; // frame identity (e.g. ImagePath + size)

        public BitmapSource[] Frames => Animation.Frames;
        public int[] FrameDelaysMs => Animation.FrameDelaysMs;
    }

    // The animation clock must not own the visuals it drives. A dynamic tile
    // can replace its Image before WPF raises Unloaded; a strong key would
    // retain both that Image and every decoded animation frame behind it.
    private static readonly List<Entry> s_entries = new();
    private static readonly Dictionary<string, WeakReference<StreamControllerEditorAnimation>> s_sharedAnimations = new();
    private static DispatcherTimer? s_timer;

    public static void Register(Image target, StreamControllerEditorAnimation anim, string signature)
    {
        if (target == null || anim == null || anim.Frames.Length == 0) return;

        // If re-registering the same signature, leave the existing timer
        // state alone so the animation doesn't jerk back to frame 0 on
        // every config-refresh pass.
        for (int i = s_entries.Count - 1; i >= 0; i--)
        {
            if (!s_entries[i].Target.TryGetTarget(out var registeredTarget))
            {
                s_entries.RemoveAt(i);
                continue;
            }

            if (!ReferenceEquals(registeredTarget, target)) continue;
            if (s_entries[i].Signature == signature) return;
            s_entries.RemoveAt(i);
        }

        // Re-registering with a new animation must not stack another copy of
        // the same Unloaded handler on the Image.
        target.Unloaded -= OnTargetUnloaded;

        // Identical keys can be visible in multiple editor surfaces. Their
        // frozen frame arrays are immutable and safe to share.
        if (!string.IsNullOrEmpty(signature)
            && s_sharedAnimations.TryGetValue(signature, out var cached)
            && cached.TryGetTarget(out var shared))
        {
            anim = shared;
        }
        else if (!string.IsNullOrEmpty(signature))
        {
            s_sharedAnimations[signature] = new WeakReference<StreamControllerEditorAnimation>(anim);
        }

        int firstDelay = Math.Max(40, anim.FrameDelaysMs.Length > 0 ? anim.FrameDelaysMs[0] : 100);
        s_entries.Add(new Entry
        {
            Target = new WeakReference<Image>(target),
            Animation = anim,
            Index = 0,
            NextAtUtc = DateTime.UtcNow.AddMilliseconds(firstDelay),
            Signature = signature,
        });
        target.Source = anim.Frames[0];
        target.Unloaded += OnTargetUnloaded;
        EnsureTimer();
    }

    public static void Unregister(Image target)
    {
        if (target == null) return;
        bool removed = false;
        for (int i = s_entries.Count - 1; i >= 0; i--)
        {
            if (!s_entries[i].Target.TryGetTarget(out var registeredTarget))
            {
                s_entries.RemoveAt(i);
                continue;
            }

            if (!ReferenceEquals(registeredTarget, target)) continue;
            s_entries.RemoveAt(i);
            removed = true;
        }
        if (removed) target.Unloaded -= OnTargetUnloaded;
        if (s_entries.Count == 0)
        {
            s_timer?.Stop();
            s_timer = null;
            s_sharedAnimations.Clear();
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

        // Iterate backwards so dead weak registrations can be compacted in
        // place without allocating a targets array on every 30 ms tick.
        for (int i = s_entries.Count - 1; i >= 0; i--)
        {
            var entry = s_entries[i];
            if (!entry.Target.TryGetTarget(out var target))
            {
                s_entries.RemoveAt(i);
                continue;
            }
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

        if (s_entries.Count == 0)
        {
            s_timer?.Stop();
            s_timer = null;
            s_sharedAnimations.Clear();
        }
    }
}
