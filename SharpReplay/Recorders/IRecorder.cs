using SharpReplay.Models;
using System;
using System.Threading.Tasks;

namespace SharpReplay.Recorders
{
    public delegate void StoppedDelegate(bool requested);

    public interface IRecorder
    {
        RecorderOptions Options { get; set; }
        bool IsRecording { get; }

        event StoppedDelegate Stopped;

        Task StartAsync();
        Task StopAsync(bool discard = false);
    }
}
