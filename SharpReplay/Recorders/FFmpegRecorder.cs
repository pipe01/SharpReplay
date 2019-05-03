﻿using Anotar.Log4Net;
using Nito.AsyncEx;
using SharpReplay.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SharpReplay.Recorders
{
    public class FFmpegRecorder : IRecorder, IFileVideoProvider
    {
        private const int SegmentInterval = 3;

        public bool IsCurationNeeded => false;
        public RecorderOptions Options { get; set; }
        public bool IsRecording { get; private set; }

        private Process FFmpeg;
        private ContinuousList<(NamedPipeServerStream Pipe, MemoryStream Stream, int Index)> Pipes;
        private readonly AsyncAutoResetEvent SegmentEvent = new AsyncAutoResetEvent();

        private int PipeNumber => (int)Math.Ceiling((float)Options.MaxReplayLengthSeconds / SegmentInterval) + 1;

        public FFmpegRecorder(RecorderOptions options)
        {
            this.Options = options;
        }

        public Task StartAsync()
        {
            Pipes = new ContinuousList<(NamedPipeServerStream, MemoryStream, int)>(PipeNumber);
            Pipes.ItemDropped += (sender, e) =>
            {
                try
                {
                    e.Pipe.Dispose();
                    e.Stream.Dispose();
                }
                catch { }
            };

            for (int i = 0; i < PipeNumber; i++)
            {
                AddPipe(i);
            }

            FFmpeg = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg.exe",
                    Arguments = $"-f gdigrab -i desktop -r {Options.Framerate} -c:v {Options.VideoCodec} -b:v {Options.MemoryBitrateMegabytes}M " +
                            $"-g {SegmentInterval * Options.Framerate} -flags -global_header -map 0 -crf 0 " +
                            $"-preset ultrafast -f segment -segment_time {SegmentInterval} -segment_format ismv " +
                            $@"-y \\.\pipe\ffpipe%d",
                    RedirectStandardInput = true,
                    RedirectStandardError = false,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            FFmpeg.Exited += delegate { IsRecording = false; };
            FFmpeg.Start();

            IsRecording = true;
            return Task.CompletedTask;
        }

        private void AddPipe(int index = -1)
        {
            if (index == -1)
                index = Pipes.Last().Index + 1;

            var pipe = new NamedPipeServerStream("ffpipe" + index, PipeDirection.In, PipeNumber, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 448 * 1024, 0);
            var mem = new MemoryStream();

            new Thread(o =>
            {
                int pipeIndex = (int)o;

                try
                {
                    pipe.WaitForConnection();
                }
                catch
                {
                    return;
                }

                LogTo.Debug("Writing to pipe index {0}", pipeIndex);
                LogTo.Debug("Pipes: " + string.Join(", ", Pipes.Select(j => j.Index)));

                if (pipeIndex == Pipes.First().Index + PipeNumber - 1)
                {
                    LogTo.Debug("Pruning pipes and adding a new one");

                    AddPipe(pipeIndex + 1);

                    LogTo.Debug("Pipes: " + string.Join(", ", Pipes.Select(j => j.Index)));
                }

                int read = 0;
                byte[] buffer = new byte[pipe.InBufferSize];

                try
                {
                    while (pipe.IsConnected && (read = pipe.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        mem.Write(buffer, 0, read);
                    }
                }
                catch (ObjectDisposedException) { }
                catch (Exception ex)
                {
                    LogTo.ErrorException("Exception on pipe " + pipeIndex, ex);
                }
                finally
                {
                    pipe.Close();
                    pipe.Dispose();
                }

                SegmentEvent.Set();
                LogTo.Debug("Thread for pipe index {0} exited", pipeIndex);
            })
            {
                IsBackground = true
            }.Start(index);

            Pipes.Add((pipe, mem, index));
        }

        public async Task StopAsync(bool discard = false)
        {
            LogTo.Info("Stopping");

            FFmpeg.StandardInput.Write("qqqqqqqqqqqq");

            if (!await FFmpeg.WaitForExitAsync(3000))
                FFmpeg.Kill();

            await Task.Delay(300);

            FFmpeg.Dispose();

            foreach (var pipe in Pipes)
            {
                pipe.Pipe.Dispose();

                if (discard)
                    pipe.Stream.Dispose();
            }

            if (discard)
                Pipes.Clear();

            GC.Collect();
        }

        public async Task WriteDataAsync(string fileName)
        {
            await StopAsync();

            string tempFolder = "./temp";

            Directory.CreateDirectory(tempFolder);

            foreach (var item in Directory.EnumerateFiles(tempFolder))
            {
                File.Delete(item);
            }

            var files = new List<string>();

            LogTo.Debug("Stream lengths: " + string.Join(", ", Pipes.Select(o => o.Stream.Length)));

            foreach (var item in Pipes)
            {
                var path = Path.Combine(tempFolder, $"segment{files.Count}.mp4");
                files.Add(path);

                using (var file = File.OpenWrite(path))
                {
                    item.Stream.Position = 0;

                    await item.Stream.CopyToAsync(file);
                    file.Flush();
                }
            }

            var ffmpeg = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg.exe",
                    Arguments = $@"-i ""concat:{string.Join("|", files)}"" -b:v {Options.OutputBitrateMegabytes}M -c:v {Options.VideoCodec} -y ""{fileName}""",
                    RedirectStandardError = false,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            ffmpeg.Start();

            await ffmpeg.WaitForExitAsync();

            await StartAsync();
        }
    }
}
