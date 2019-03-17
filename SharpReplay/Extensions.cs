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
            var tcs = new TaskCompletionSource<object>();
            process.EnableRaisingEvents = true;
            process.Exited += (sender, args) => tcs.TrySetResult(null);
            if (cancellationToken != default)
                cancellationToken.Register(tcs.SetCanceled);

            return tcs.Task;
        }

        public static bool IsEither<T>(this T obj, params T[] possibilities)
            => possibilities.Any(o => obj.Equals(o));
    }
}
