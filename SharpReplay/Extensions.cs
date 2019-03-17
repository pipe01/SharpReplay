using NHotkey;
using NHotkey.Wpf;
using SharpReplay.UI;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SharpReplay
{
    public static class Extensions
    {
        public static void ReadCompletely(this Stream stream, byte[] buffer, int offset, int count)
        {
            int remaining = count;

            while (remaining > 0)
            {
                int read = stream.Read(buffer, offset, remaining);
                
                offset += read;
                remaining -= read;
            }
        }

        public static Task WaitForExitAsync(this Process process, CancellationToken cancellationToken = default)
        {
            if (process.HasExited)
                return Task.CompletedTask;

            var tcs = new TaskCompletionSource<object>();
            process.EnableRaisingEvents = true;
            process.Exited += (sender, args) => tcs.TrySetResult(null);
            if (cancellationToken != default)
                cancellationToken.Register(tcs.SetCanceled);

            return tcs.Task;
        }

        public static bool IsEither<T>(this T obj, params T[] possibilities)
            => possibilities.Any(o => obj.Equals(o));

        public static void AddOrReplace(this HotkeyManager man, string name, Hotkey hotkey, EventHandler<HotkeyEventArgs> handler)
            => man.AddOrReplace(name, hotkey.Key, hotkey.Modifiers, handler);

        public static string GetH264Suffix(this RecorderOptions.HardwareAccel accel)
            => accel == RecorderOptions.HardwareAccel.AMD ? "amf" :
               accel == RecorderOptions.HardwareAccel.NVIDIA ? "nvenc" : null;

        public static int ToInt32BigEndian(this byte[] data, int offset = 0)
            => data[offset + 3] | data[offset + 2] << 8 | data[offset + 1] << 16 | data[offset] << 24;

        public static bool Contains<T>(this T[] haystack, T[] needles)
        {
            for (int i = 0; i < haystack.Length; i++)
            {
                bool contained = true;

                for (int j = 0; j < needles.Length; j++)
                {
                    if (!haystack[i + j].Equals(needles[j]))
                    {
                        contained = false;
                        break;
                    }
                }

                if (contained)
                    return true;
            }

            return false;
        }

        public static T[] CloneArray<T>(this T[] arr)
        {
            var newArr = new T[arr.Length];
            Array.Copy(arr, newArr, arr.Length);
            return newArr;
        }
    }
}
