using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpReplay
{
    public class FragmentParser
    {
        [DebuggerDisplay("{Name}: {Data.Length} bytes")]
        public readonly struct Box
        {
            public string Name { get; }
            public byte[] Data { get; }

            public Box(string name, in byte[] data)
            {
                this.Name = name;
                this.Data = data;
            }
        }

        private readonly Stream BaseStream;

        public FragmentParser(Stream stream)
        {
            this.BaseStream = stream;
        }

        public IEnumerable<Box> GetBoxes()
        {
            byte[] lengthB = new byte[4];
            byte[] nameB = new byte[4];
            byte[] data;
            var asd = new List<byte>();

            while (true)
            {
                int read = BaseStream.Read(lengthB, 0, 4);

                int length = BitConverter.ToInt32(new[] { lengthB[3], lengthB[2], lengthB[1], lengthB[0] }, 0);

                data = new byte[length];

                BaseStream.Read(nameB, 0, 4);
                string name = Encoding.UTF8.GetString(nameB, 0, 4);

                BaseStream.ReadCompletely(data, length);

                yield return new Box(name, data);
            }
        }
    }
}
