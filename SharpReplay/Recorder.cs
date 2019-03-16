using Anotar.Log4Net;
using Nito.AsyncEx;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SharpReplay
{
    public class Recorder
    {
        private readonly struct Fragment
        {
            public DateTimeOffset Time { get; }
            public byte[] Data { get; }

            public Fragment(DateTimeOffset time, byte[] data)
            {
                this.Time = time;
                this.Data = data;
            }
        }

        public bool IsRecording { get; private set; }

        public int MaxReplayLengthSeconds { get; set; } = 30;
        public int Framerate { get; set; } = 30;
        public bool RecordSystemAudio { get; set; } = false;

        private Process FFmpeg;
        private NamedPipeServerStream OutputPipe;
        private AsyncAutoResetEvent FragmentEvent = new AsyncAutoResetEvent();

        private ContinuousList<Fragment> Fragments;
        private MemoryStream CurrentFragment = new MemoryStream();
        private byte[] Mp4Header = null;

        public async Task StartAsync()
        {
            if (IsRecording)
            {
                LogTo.Error("Tried to start recording while already recording");
                throw new InvalidOperationException("Already recording");
            }

            LogTo.Info("Start recording");

            Fragments = new ContinuousList<Fragment>(5);

            await StartPipeAndProcess();

            new Thread(FragmentsThread)
            {
                IsBackground = true
            }.Start();

            IsRecording = true;
        }

        public async Task WriteReplayAsync(Stream toStream)
        {
            if (!IsRecording)
                throw new InvalidOperationException("Not recording");

            LogTo.Info("Writing replay");

            await FragmentEvent.WaitAsync();

            toStream.Write(Mp4Header, 0, Mp4Header.Length);

            int count = 0;
            foreach (var item in Fragments.Where(o => (DateTimeOffset.Now - o.Time).TotalSeconds < MaxReplayLengthSeconds))
            {
                File.WriteAllBytes($"frag{count}.bin", item.Data);
                toStream.Write(item.Data, 0, item.Data.Length);

                count++;
            }

            LogTo.Info("Written {0} fragments", count);
        }

        public async Task StopAsync(bool waitForFragment = true)
        {
            if (waitForFragment)
                await FragmentEvent.WaitAsync();

            IsRecording = false;

            FFmpeg.Close();
            FFmpeg.Dispose();
            OutputPipe.Dispose();

            Fragments.Clear();
            CurrentFragment.Dispose();
            CurrentFragment = new MemoryStream();

            Mp4Header = null;

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
                            @"-f dshow -i audio=""@device_cm_{33D9A762-90C8-11D0-BD43-00A0C911CE86}\wave_{5F5B258C-644B-4ACA-B5DA-26733B50300E}"" " : "") +
                             "-g 27 -strict experimental -crf 0 -preset ultrafast -b:v 4M -c:v h264_amf " +
                           $@"-r {Framerate} -f mp4 -movflags frag_keyframe+empty_moov -y \\.\pipe\ffpipe",
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

        private Stream OutFile;
        private void FragmentsThread()
        {
            OutFile = File.OpenWrite("out.flv");
            byte[] buffer = new byte[OutputPipe.InBufferSize];

            while (OutputPipe.IsConnected)
            {
                var read = OutputPipe.Read(buffer, 0, buffer.Length);

                if (read > 0)
                {
                    OutFile.Write(buffer, 0, read);
                    //string box = Encoding.UTF8.GetString(buffer, 4, 4);
                    //LogTo.Debug(box);

                    //if (box == "moof")
                    //{
                    //    if (Mp4Header == null)
                    //        Mp4Header = CurrentFragment.ToArray();
                    //    else
                    //        Fragments.Add(new Fragment(DateTimeOffset.Now, CurrentFragment.ToArray()));

                    //    LogTo.Debug("Fragment length: " + CurrentFragment.Length);
                        
                    //    CurrentFragment.Position = 0;
                    //    FragmentEvent.Set();
                    //}

                    //CurrentFragment.Write(buffer, 0, read);
                }
            }
        }
    }
}
