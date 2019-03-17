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

        public IEnumerable<Mp4Box> Children
        {
            get
            {
                for (int i = 8; i < Data.Length;)
                {
                    int childLen = Data.ToInt32BigEndian(i);
                    string childName = Encoding.UTF8.GetString(Data, i + 4, 4);

                    byte[] childData = new byte[childLen];
                    Buffer.BlockCopy(Data, i, childData, 0, childLen);

                    i += childLen;

                    yield return new Mp4Box(childName, childData);
                }
            }
        }

        public Mp4Box this[string childName] => Children.First(o => o.Name == childName);
    }

    public class BoxParser : IEnumerable<Mp4Box>
    {
        private readonly Stream BaseStream;

        public BoxParser(Stream stream)
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

                int length = lengthB.ToInt32BigEndian();

                if (length == 0)
                    continue;

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
                    if (!(ex is ObjectDisposedException))
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
