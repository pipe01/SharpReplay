using SharpReplay.Models;
using System.IO;
using System.Threading.Tasks;

namespace SharpReplay.Recorders
{
    public interface IRecorder
    {
        RecorderOptions Options { get; set; }
        bool IsRecording { get; }

        Task StartAsync();
        Task StopAsync(bool discard = false);
    }
}
