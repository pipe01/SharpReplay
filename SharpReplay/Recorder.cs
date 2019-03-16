using Anotar.Log4Net;
using Nito.AsyncEx;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text.RegularExpressions;
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

        private readonly static Regex FrameTimeRegex = new Regex("(?<=dts=).*?(?= )");

        public bool IsRecording { get; private set; }

        public int MaxReplayLengthSeconds { get; set; } = 5;
        public int Framerate { get; set; } = 30;
        public bool RecordAudio { get; set; }
        public string AudioDevice { get; set; }
        public string VideoCodec { get; set; } = "h264_amf";

        private Process FFmpeg;
        private NamedPipeServerStream OutputPipe;
        private Stopwatch Timer = new Stopwatch();

        private AsyncAutoResetEvent KeyframeEvent = new AsyncAutoResetEvent();
        private ContinuousList<Fragment> Fragments;
        private byte[] Mp4Header;
        private List<Mp4Box> Footer;
        private DateTimeOffset LastReportedTime;

        public async Task StartAsync()
        {
            if (IsRecording)
            {
                LogTo.Error("Tried to start recording while already recording");
                throw new InvalidOperationException("Already recording");
            }

            LogTo.Info("Start recording");

            Fragments = new ContinuousList<Fragment>(200);
            Footer = new List<Mp4Box>();
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

        private async Task StartPipeAndProcess()
        {
            LogTo.Debug("Creating pipe");

            OutputPipe = new NamedPipeServerStream("ffpipe", PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 448 * 1024, 0);

            FFmpeg = new Process();
            FFmpeg.StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg.exe",
                Arguments = $"-f gdigrab -framerate {Framerate} -r {Framerate} -i desktop " + (RecordAudio ?
                           $@"-f dshow -i audio=""{AudioDevice}"" -b:a 128k " : "") +
                            $"-g 10 -strict experimental -crf 0 -preset ultrafast -b:v 5M -c:v {VideoCodec} " +
                           $@"-r {Framerate} -f ismv -movflags frag_keyframe -y \\.\pipe\ffpipe",
                RedirectStandardInput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            LogTo.Debug("Launching FFmpeg with arguments:");
            LogTo.Debug(FFmpeg.StartInfo.Arguments);

            FFmpeg.Start();
            FFmpeg.PriorityClass = ProcessPriorityClass.High;

            LogTo.Debug("Waiting for FFmpeg to connect to pipe");

            new Thread(() =>
            {
                while (!FFmpeg.HasExited)
                {
                    var line = FFmpeg.StandardError.ReadLine();

                    if (line == null)
                        continue;

                    LogTo.Debug($"[FFMPEG] {line}");

                    if (line.StartsWith("  dts="))
                    {
                        string timeStr = FrameTimeRegex.Match(line).Value;
                        long time = (long)(double.Parse(timeStr, CultureInfo.InvariantCulture) * 1000);

                        LastReportedTime = DateTimeOffset.FromUnixTimeMilliseconds(time);
                        KeyframeEvent.Set();
                    }
                }
            })
            {
                IsBackground = true
            }.Start();

            await OutputPipe.WaitForConnectionAsync();

            FFmpeg.StandardInput.Write("-h");
        }

        public async Task WriteReplayAsync()
        {
            LogTo.Debug("Current time: {0}", DateTimeOffset.Now.ToUnixTimeMilliseconds());
            LogTo.Debug("Last fragment time: {0}", Fragments.Last.Time);

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
            await StartAsync();
        }

        public async Task WriteReplayAsync(Stream toStream, bool closeStream = true)
        {
            if (!IsRecording)
                throw new InvalidOperationException("Not recording");

            Timer.Stop();

            LogTo.Info("Writing replay");

            LogTo.Debug("Waiting for one more fragment");
            await KeyframeEvent.WaitAsync();

            toStream.Write(Mp4Header, 0, Mp4Header.Length);

            await StopAsync();
            
            var frags = Fragments.Where(o => (Fragments.Last.Time - o.Time).TotalSeconds < MaxReplayLengthSeconds);
            int count = 0;
            foreach (var item in frags)
            {
                foreach (var box in item.Boxes)
                {
                    toStream.Write(box.Data, 0, box.Data.Length);
                }

                count++;
            }

            byte[] footer = Footer.SelectMany(o => o.Data).ToArray();
            toStream.Write(footer, 0, footer.Length);

            if (closeStream)
                toStream.Dispose();

            LogTo.Info("Written {0} fragments", count);
        }

        public async Task StopAsync()
        {
            LogTo.Info("Stopping");

            FFmpeg.StandardInput.Write("qqqqqqqqqqqqqqqq");
            await FFmpeg.WaitForExitAsync();

            Timer.Stop();
            IsRecording = false;

            FFmpeg.Close();
            FFmpeg.Dispose();
            OutputPipe.Dispose();
            
            GC.Collect();
        }

        private void FragmentsThread()
        {
            byte[] buffer = new byte[OutputPipe.InBufferSize];

            Timer.Restart();

            var boxes = new FragmentParser(OutputPipe);

            var headerBoxes = boxes.Take(2).ToArray();
            Mp4Header = headerBoxes.SelectMany(o => o.Data).ToArray();

            Mp4Box lastMoof = default;

            foreach (var box in boxes)
            {
                LogTo.Debug("Box: " + box.Name);

                if (box.Name == "moof")
                {
                    lastMoof = box;
                }
                else if (box.Name == "mdat")
                {
                    Fragments.Add(new Fragment(LastReportedTime, new[] { lastMoof, box }));
                }
                else
                {
                    Footer.Add(box);
                }
            }
        }
    }
}
