using Anotar.Log4Net;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SharpReplay
{
    public class Recorder
    {
        private readonly struct Fragment
        {
            public DateTimeOffset Time { get; }
            public Mp4Box[] Boxes { get; }

            public Fragment(DateTimeOffset time, Mp4Box[] boxes)
            {
                this.Time = time;
                this.Boxes = boxes;
            }
        }

        public bool IsRecording { get; private set; }

        public int MaxReplayLengthSeconds { get; set; } = 5;
        public int Framerate { get; set; } = 30;
        public bool RecordSystemAudio { get; set; } = true;
        public string VideoCodec { get; set; } = "h264_amf";

        private Process FFmpeg;
        private NamedPipeServerStream OutputPipe;

        private ContinuousList<Fragment> Fragments;
        private byte[] Mp4Header;

        public async Task StartAsync()
        {
            if (IsRecording)
            {
                LogTo.Error("Tried to start recording while already recording");
                throw new InvalidOperationException("Already recording");
            }

            LogTo.Info("Start recording");

            Fragments = new ContinuousList<Fragment>(200);
            Mp4Header = null;

            await StartPipeAndProcess();

            var thread = new Thread(FragmentsThread)
            {
                IsBackground = true,
                Name = "RecorderThread"
            };

            thread.Start();

            LogTo.Debug("Thread started with ID {0}", thread.ManagedThreadId);

            IsRecording = true;
        }

        public async Task WriteReplayAsync()
        {
            string outPath = $"./out/{DateTime.Now:yyyyMMdd_hhmmss}.mp4";

            LogTo.Info("Writing replay to \"{0}\"", outPath);

            Directory.CreateDirectory(Path.GetDirectoryName(outPath));

            var pipe = new NamedPipeServerStream("outpipe", PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 448 * 1024);

            var curator = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg.exe",
                    Arguments = $@"-i \\.\pipe\outpipe -c:v {VideoCodec} -b:a 128k {outPath}",
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    CreateNoWindow = true
                }
            };
            curator.Start();

            await pipe.WaitForConnectionAsync();
            await WriteReplayAsync(pipe, false);

            curator.StandardInput.Write("qqqqqqqqqqqqqqq");
            await curator.WaitForExitAsync();

            pipe.Dispose();

            LogTo.Info("Done writing");
        }

        public async Task WriteReplayAsync(Stream toStream, bool closeStream = true)
        {
            if (!IsRecording)
                throw new InvalidOperationException("Not recording");

            LogTo.Info("Writing replay");

            toStream.Write(Mp4Header, 0, Mp4Header.Length);

            await StopAsync();

            int count = 0;
            foreach (var item in Fragments.Where(o => (DateTimeOffset.Now - o.Time).TotalSeconds <= MaxReplayLengthSeconds))
            {
                foreach (var box in item.Boxes)
                {
                    toStream.Write(box.Data, 0, box.Data.Length);
                }

                count++;
            }

            if (closeStream)
                toStream.Dispose();

            LogTo.Info("Written {0} fragments", count);

            //await StartAsync();
        }

        public async Task StopAsync()
        {
            LogTo.Info("Stopping");

            FFmpeg.StandardInput.Write("qqqqqqqqqqqqqqqq");
            await FFmpeg.WaitForExitAsync();

            IsRecording = false;

            FFmpeg.Close();
            FFmpeg.Dispose();
            OutputPipe.Dispose();
            
            GC.Collect();
        }

        private async Task StartPipeAndProcess()
        {
            LogTo.Debug("Creating pipe");

            OutputPipe = new NamedPipeServerStream("ffpipe", PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 448 * 1024, 0);
            
            FFmpeg = new Process();
            FFmpeg.StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg.exe",
                Arguments = $"-f gdigrab -framerate {Framerate} -r {Framerate} -i desktop " + (RecordSystemAudio ?
                            @"-f dshow -i audio=""@device_cm_{33D9A762-90C8-11D0-BD43-00A0C911CE86}\wave_{5F5B258C-644B-4ACA-B5DA-26733B50300E}"" -b:a 128k " : "") +
                            $"-g 27 -strict experimental -crf 0 -preset ultrafast -b:v 4M -c:v {VideoCodec} " +
                           $@"-r {Framerate} -f ismv -movflags frag_keyframe -y \\.\pipe\ffpipe",
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            LogTo.Debug("Launching FFmpeg with arguments:");
            LogTo.Debug(FFmpeg.StartInfo.Arguments);

            FFmpeg.Start();
            FFmpeg.PriorityClass = ProcessPriorityClass.High;

            LogTo.Debug("Waiting for FFmpeg to connect to pipe");

            await OutputPipe.WaitForConnectionAsync();
        }

        private void FragmentsThread()
        {
            byte[] buffer = new byte[OutputPipe.InBufferSize];

            var boxes = new FragmentParser(OutputPipe);

            var headerBoxes = boxes.Take(2).ToArray();
            Mp4Header = headerBoxes.SelectMany(o => o.Data).ToArray();

            Mp4Box lastMoof = default;

            foreach (var item in boxes)
            {
                LogTo.Debug("Box: " + item.Name);

                if (item.Name == "moof")
                {
                    lastMoof = item;
                }
                else
                {
                    Fragments.Add(new Fragment(DateTimeOffset.Now, new[] { lastMoof, item }));
                }
            }
        }
    }
}
