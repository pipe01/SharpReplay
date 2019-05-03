using System.IO;
using System.Threading.Tasks;

namespace SharpReplay.Recorders
{
    public interface IVideoProvider
    {
        bool IsCurationNeeded { get; }

        Task WriteDataAsync(Stream output);
    }
}
