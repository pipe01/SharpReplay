using ScreenRecorderLib;
using System.IO;
using System.Threading.Tasks;
using RecorderOptions = SharpReplay.Models.RecorderOptions;

namespace SharpReplay.Recorders
{
    public class LibRecorder : MP4Recorder
    {
        private Recorder Recorder;
        private Stream OutStream;

        public LibRecorder(RecorderOptions options) : base(options)
        {
        }

        public override Task StopAsync()
        {
            IsRecording = false;

            Recorder.Stop();
            OutStream.Dispose();

            return Task.CompletedTask;
        }

        protected override Task<Stream> StartStreamAsync()
        {
            Recorder = Recorder.CreateRecorder(new ScreenRecorderLib.RecorderOptions
            {
                VideoOptions = new VideoOptions
                {
                    Bitrate = 8 * 1024 * 1024,
                    BitrateMode = BitrateControlMode.Quality,
                    Framerate = Options.Framerate,
                    IsFixedFramerate = true,
                    IsMousePointerEnabled = true,
                    Quality = 100
                },
                IsFragmentedMp4Enabled = true,
                IsHardwareEncodingEnabled = true,
                RecorderMode = RecorderMode.Video
            });
            Recorder.Record(OutStream = new MemoryStream()); //File.OpenWrite("out.mp4")

            return Task.FromResult<Stream>(OutStream);
        }
    }
}
