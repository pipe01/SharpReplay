using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Anotar.Log4Net;
using Nito.AsyncEx;
using SharpReplay.Models;
using Timer = System.Timers.Timer;

namespace SharpReplay.Recorders
{
    public abstract class MP4Recorder : IRecorder
    {
        public RecorderOptions Options { get; set; }
        public bool IsRecording { get; protected set; }

        protected virtual int InBufferSize => 480 * 1024;

        private Stream InStream;
        private ContinuousList<Fragment> Fragments;
        private byte[] Mp4Header;

        private int FragmentCounter;
        private readonly Timer FragmentTimer;

        private readonly AsyncManualResetEvent FragmentEvent = new AsyncManualResetEvent();

        protected MP4Recorder(RecorderOptions options)
        {
            this.Options = options;

            this.FragmentTimer = new Timer(5000);
            this.FragmentTimer.Elapsed += this.FragmentTimer_Elapsed;
            this.FragmentTimer.Start();
        }

        public async Task StartAsync()
        {
            if (IsRecording)
            {
                LogTo.Error("Tried to start recording while already recording");
                throw new InvalidOperationException("Already recording");
            }

            LogTo.Info("Start recording");

            Fragments = new ContinuousList<Fragment>(10);
            Mp4Header = null;

            InStream = await StartStreamAsync();

            var thread = new Thread(FragmentsThread)
            {
                IsBackground = true,
                Name = "RecorderThread"
            };

            thread.Start();

            LogTo.Debug("Thread started with ID {0}", thread.ManagedThreadId);

            IsRecording = true;
        }

        public virtual Task StopAsync()
        {
            IsRecording = false;

            return Task.CompletedTask;
        }

        protected abstract Task<Stream> StartStreamAsync();

        public async Task WriteDataAsync(Stream output)
        {
            LogTo.Info("Writing replay");

            var lastFrag = Fragments.Last();

            LogTo.Debug("Current time: {0}", DateTimeOffset.Now.ToUnixTimeMilliseconds());
            LogTo.Debug("Last fragment time: {0}", lastFrag.Time.ToUnixTimeMilliseconds());

            LogTo.Debug("Writing fragments");

            await output.WriteAsync(Mp4Header, 0, Mp4Header.Length);

            var frags = Fragments.Where(o => (lastFrag.Time - o.Time).TotalSeconds < Options.MaxReplayLengthSeconds);
            int count = 0;
            foreach (var item in frags)
            {
                foreach (var box in item.Boxes)
                {
                    await output.WriteAsync(box.Data, 0, box.Data.Length);
                }

                count++;
            }

            LogTo.Info("Written {0} fragments", count);
        }

        private void FragmentTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (!IsRecording)
                return;

            double fragmentsPerSecond = FragmentCounter / (FragmentTimer.Interval / 1000);
            FragmentCounter = 0;

            LogTo.Debug("Fragments per second: {0}", fragmentsPerSecond);

            int totalFragmentsNeeded = (int)Math.Ceiling(fragmentsPerSecond * (Options.MaxReplayLengthSeconds + 2));

            if (totalFragmentsNeeded > Fragments.Capacity)
            {
                LogTo.Debug("Resizing fragments buffer to {0} in order to to fit {1} frags/s", totalFragmentsNeeded, fragmentsPerSecond);
                Fragments.SetCapacity(totalFragmentsNeeded);
            }
        }

        private void FragmentsThread()
        {
            byte[] buffer = new byte[InBufferSize];

            var boxes = new BoxParser(InStream);

            var headerBoxes = boxes.Take(2).ToArray();
            Mp4Header = headerBoxes.SelectMany(o => o.Data).ToArray();

            Mp4Box[] fragBoxes = new Mp4Box[Options.AudioDevices.Length > 0 ? 4 : 2];
            int boxCounter = 0;

            try
            {
                foreach (var box in boxes)
                {
                    if (box.Name == "moof" || box.Name == "mdat")
                    {
                        fragBoxes[boxCounter++] = box;

                        if (boxCounter == fragBoxes.Length)
                        {
                            boxCounter = 0;

                            Fragments.Add(new Fragment(DateTimeOffset.Now, fragBoxes));
                            FragmentCounter++;
                            FragmentEvent.Set();

                            fragBoxes = new Mp4Box[fragBoxes.Length];

                            LogTo.Debug("New fragment");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (!(ex is ObjectDisposedException))
                    throw;
            }
        }
    }
}
