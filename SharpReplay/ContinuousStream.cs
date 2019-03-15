using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpReplay
{
    public class ContinuousStream : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => true;
        public override bool CanWrite => true;

        public override long Length => 0;
        public override long Position { get; set; }

        private int CurrentChunkIndex;

        private MemoryStream CurrentChunk => Chunks[CurrentChunkIndex];
        private int SpaceLeftInChunk => (int)(CurrentChunk.Capacity - CurrentChunk.Position);

        private string All => string.Join(" ", Chunks.Select(o => o.ToArray()).SelectMany(o => o).Select(o => o.ToString()));

        private readonly MemoryStream[] Chunks;

        public ContinuousStream(int chunkCount, int chunkSize)
        {
            this.Chunks = Enumerable.Range(0, chunkCount).Select(_ => new MemoryStream(chunkSize)).ToArray();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
            => throw new InvalidOperationException();

        public override long Seek(long offset, SeekOrigin origin)
        {
            return offset;
        }

        public override void SetLength(long value)
            => throw new InvalidOperationException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            int written = 0;

            while (written < count)
            {
                int remaining = count - written;

                if (SpaceLeftInChunk >= remaining)
                {
                    CurrentChunk.Write(buffer, written, remaining);
                    written += count;
                }
                else
                {
                    int sizeToWrite = Math.Min(remaining, SpaceLeftInChunk);

                    CurrentChunk.Write(buffer, written, sizeToWrite);
                    written += sizeToWrite;

                    NextChunk();
                }
            }
        }

        public new void CopyTo(Stream destination)
        {
            int startChunk = CurrentChunkIndex;
            int startPos = (int)CurrentChunk.Position;
            int i = startChunk + 1;

            if (i == Chunks.Length)
                i = 0;

            byte[] firstChunkData = CurrentChunk.ToArray();
            destination.Write(firstChunkData, (int)CurrentChunk.Position, (int)(firstChunkData.Length - CurrentChunk.Position));

            do
            {
                var chunk = Chunks[i];
                byte[] data = chunk.ToArray();

                destination.Write(data, 0, (int)chunk.Length);

                i++;
                if (i == Chunks.Length)
                {
                    i = 0;
                }
            }
            while (i != startChunk);

            destination.Write(firstChunkData, 0, (int)CurrentChunk.Position);
        }

        public new void Dispose()
        {
            foreach (var item in Chunks)
            {
                item.Dispose();
            }
        }

        private void NextChunk()
        {
            CurrentChunkIndex++;

            if (CurrentChunkIndex >= Chunks.Length)
            {
                CurrentChunkIndex = 0;
            }

            CurrentChunk.Position = 0;
        }
    }
}
