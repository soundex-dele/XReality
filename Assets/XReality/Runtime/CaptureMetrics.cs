using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace XReality
{
    // Uses fixed-size histograms. No camera frames, landmarks, or individual pose samples are stored.
    internal sealed class CaptureMetrics
    {
        readonly int[] render = new int[251];
        readonly int[] inference = new int[251];
        readonly int[] clean720 = new int[251];
        double started, lastFrame;
        int renders, frames, visible;
        int interruptions, reacquisitions;
        bool lastSeen, everSeen, wasClean720, wasUnfocused;
        double cleanSeconds, continuousClean, longestClean, unfocusedSeconds, nextMemorySample;
        int cleanFrames, unfocusedFrames, cleanCameraFrames, cleanFaceFrames;
        int memorySamples;
        long firstMemory, lastMemory, peakMemory;
        double worstCleanMs;
        internal int MemorySamples => memorySamples;
        public bool Running { get; private set; }
        public string Report { get; private set; }

        internal void Begin(double now)
        {
            Report = null;
            started = lastFrame = now;
            renders = frames = visible = interruptions = reacquisitions = 0;
            lastSeen = everSeen = wasClean720 = wasUnfocused = false;
            cleanSeconds = continuousClean = longestClean = unfocusedSeconds = worstCleanMs = 0;
            cleanFrames = unfocusedFrames = cleanCameraFrames = cleanFaceFrames = memorySamples = 0;
            firstMemory = lastMemory = peakMemory = 0;
            nextMemorySample = now;
            Array.Clear(render, 0, render.Length);
            Array.Clear(clean720, 0, clean720.Length);
            Array.Clear(inference, 0, inference.Length);
            Running = true;
        }

        static void Observe(int[] histogram, double milliseconds)
        {
            int bucket = (int)Math.Max(0, Math.Min(histogram.Length - 1, Math.Ceiling(milliseconds)));
            histogram[bucket]++;
        }

        static string P95(int[] histogram, int count)
        {
            if (count == 0) return "n/a";
            int target = (int)Math.Ceiling(count * .95);
            for (int i = 0; i < histogram.Length; i++)
            {
                target -= histogram[i];
                if (target <= 0) return i == histogram.Length - 1 ? ">=250" : i.ToString(CultureInfo.InvariantCulture);
            }
            return "n/a";
        }

        // Count only intervals bracketed by the same clean, 1280x720 output state.
        internal void UpdateFrame(double now, bool clean = false, bool unfocused = false)
        {
            if (!Running || now <= lastFrame) return;
            double elapsed = now - lastFrame;
            Observe(render, elapsed * 1000);
            if (clean && wasClean720)
            {
                cleanFrames++;
                cleanSeconds += elapsed;
                continuousClean += elapsed;
                longestClean = Math.Max(longestClean, continuousClean);
                worstCleanMs = Math.Max(worstCleanMs, elapsed * 1000);
                Observe(clean720, elapsed * 1000);
                if (unfocused && wasUnfocused)
                {
                    unfocusedFrames++;
                    unfocusedSeconds += elapsed;
                }
            }
            else continuousClean = 0;
            lastFrame = now;
            wasClean720 = clean;
            wasUnfocused = unfocused;
            renders++;
        }

        internal void SampleMemory(double now)
        {
            if (!Running || now < nextMemorySample) return;
            // Keep the original cadence; skip missed slots rather than sampling in bursts.
            nextMemorySample += 10 * (1 + Math.Floor((now - nextMemorySample) / 10));
            long bytes = ReadWorkingSet();
            if (bytes <= 0) return;
            if (memorySamples++ == 0) firstMemory = bytes;
            lastMemory = bytes;
            peakMemory = Math.Max(peakMemory, bytes);
        }

        static long ReadWorkingSet()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            // Unity's managed Process.WorkingSet64 can return zero in the Windows player.
            // GetCurrentProcess returns a pseudo-handle; it must not be closed.
            try
            {
                uint size = (uint)Marshal.SizeOf<ProcessMemoryCounters>();
                var counters = new ProcessMemoryCounters { Size = size };
                if (GetProcessMemoryInfo(GetCurrentProcess(), ref counters, size))
                {
                    ulong bytes = counters.WorkingSet.ToUInt64();
                    if (bytes > 0 && bytes <= long.MaxValue) return (long)bytes;
                }
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
#endif
            try
            {
                using var process = System.Diagnostics.Process.GetCurrentProcess();
                process.Refresh();
                return process.WorkingSet64;
            }
            catch (System.ComponentModel.Win32Exception) { }
            catch (InvalidOperationException) { }
            catch (NotSupportedException) { }
            return 0; // Diagnostics are optional; never interrupt capture.
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [StructLayout(LayoutKind.Sequential)]
        struct ProcessMemoryCounters
        {
            internal uint Size, PageFaultCount;
            internal UIntPtr PeakWorkingSet, WorkingSet, PeakPagedPool, PagedPool;
            internal UIntPtr PeakNonPagedPool, NonPagedPool, Pagefile, PeakPagefile;
        }

        [DllImport("kernel32.dll", ExactSpelling = true)]
        static extern IntPtr GetCurrentProcess();

        [DllImport("psapi.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetProcessMemoryInfo(IntPtr process, ref ProcessMemoryCounters counters, uint size);
#endif

        static string Rate(int frames, double seconds) => seconds > 0
            ? (frames / seconds).ToString("F2", CultureInfo.InvariantCulture) : "n/a";

        static string MiB(long bytes) => (bytes / (1024.0 * 1024)).ToString("F1", CultureInfo.InvariantCulture);

        internal void Track(bool seen)
        {
            if (lastSeen == seen) return;
            if (seen && everSeen) reacquisitions++;
            if (!seen) interruptions++;
            everSeen |= seen;
            lastSeen = seen;
        }

        internal void Processed(double elapsedMs, bool seen)
        {
            Observe(inference, elapsedMs);
            frames++;
            if (seen) visible++;
            if (wasClean720)
            {
                cleanCameraFrames++;
                if (seen) cleanFaceFrames++;
            }
            Track(seen);
        }

        internal void End(double now, string reason)
        {
            if (!Running) return;
            double seconds = Math.Max(.001, now - started);
            string core = FormattableString.Invariant($"XReality {Application.version} capture QA (schema 2)\nSeconds: {seconds:F2}\nMean player FPS: {Rate(renders, seconds)}\nP95 player frame interval (ms): {P95(render, renders)}\nCamera frames processed: {frames}\nFrames with face: {visible}\nP95 camera read and inference (ms): {P95(inference, frames)}\nFace interruptions: {interruptions}\nFace reacquisitions: {reacquisitions}\nEnd reason: {reason}\n");
            string output = FormattableString.Invariant($"720p clean output seconds: {cleanSeconds:F2}\n720p clean output longest continuous seconds: {longestClean:F2}\n720p clean output mean FPS: {Rate(cleanFrames, cleanSeconds)}\n720p clean output P95 frame interval (ms): {P95(clean720, cleanFrames)}\n720p clean output longest frame interval (ms): {worstCleanMs:F1}\n720p clean output camera frames processed: {cleanCameraFrames}\n720p clean output frames with face: {cleanFaceFrames}\n720p clean output unfocused seconds: {unfocusedSeconds:F2}\n720p clean output unfocused mean FPS: {Rate(unfocusedFrames, unfocusedSeconds)}\n");
            string memory = memorySamples == 0 ? "n/a" : $"{MiB(firstMemory)} / {MiB(lastMemory)} / {MiB(peakMemory)}";
            Report = core + output + FormattableString.Invariant($"Working-set samples: {memorySamples}\nWorking set first / last / sampled peak (MiB): {memory}\n");
            Running = false;
        }

        internal string Export()
        {
            if (Report == null) throw new InvalidOperationException("No stopped capture session to report.");
            string folder = Path.Combine(Application.persistentDataPath, "Diagnostics");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "capture-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + ".txt");
            File.WriteAllText(path, Report);
            return folder;
        }
    }
}
