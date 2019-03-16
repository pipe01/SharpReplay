using System.IO;

namespace SharpReplay
{
    public static class Extensions
    {
        public static void ReadCompletely(this Stream stream, byte[] buffer, int count)
        {
            int offset = 0;
            int remaining = count;

            while (true)
            {
                int read = stream.Read(buffer, offset, remaining);

                if (read < count)
                {
                    offset += read;
                    remaining -= read;
                }
                else
                {
                    break;
                }
            }
        }
    }
}
