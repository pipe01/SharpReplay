using Anotar.Log4Net;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpReplay
{
    [DebuggerDisplay("{Name}: {Data.Length} bytes")]
    public readonly struct Mp4Box
    {
        public string Name { get; }
        public byte[] Data { get; }

        public Mp4Box(string name, in byte[] data)
        {
            this.Name = name;
            this.Data = data;
        }
    }

    public class FragmentParser : IEnumerable<Mp4Box>
    {
        private readonly Stream BaseStream;

        public FragmentParser(Stream stream)
        {
            this.BaseStream = stream;
        }

        public IEnumerable<Mp4Box> GetBoxes()
        {
            byte[] lengthB = new byte[4];
            byte[] nameB = new byte[4];
            byte[] data;

            while (true)
            {
                int read = BaseStream.Read(lengthB, 0, 4);

                int length = BitConverter.ToInt32(new[] { lengthB[3], lengthB[2], lengthB[1], lengthB[0] }, 0);

                data = new byte[length];

                BaseStream.Read(nameB, 0, 4);
                string name = Encoding.UTF8.GetString(nameB, 0, 4);

                Buffer.BlockCopy(lengthB, 0, data, 0, 4);
                Buffer.BlockCopy(nameB, 0, data, 4, 4);

                try
                {
                    BaseStream.ReadCompletely(data, 8, length - 8);
                }
                catch (Exception ex)
                {
                    LogTo.FatalException("Recording error", ex);
                    yield break;
                }

                yield return new Mp4Box(name, data);
            }
        }

        public IEnumerator<Mp4Box> GetEnumerator() => GetBoxes().GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
