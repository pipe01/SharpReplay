using System;

namespace SharpReplay.Models
{
    public class Fragment
    {
        public DateTimeOffset Time { get; }
        public Mp4Box[] Boxes { get; }

        public Fragment(DateTimeOffset time, Mp4Box[] boxes)
        {
            this.Time = time;
            this.Boxes = boxes;
        }
    }
}
