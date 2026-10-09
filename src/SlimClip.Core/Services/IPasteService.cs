using System.Threading.Tasks;
using SlimClip.Core.Models;

namespace SlimClip.Core.Services;

public interface IPasteService
{
    Task PasteAsync(ClipboardItem item, int delayMs);
    Task SetClipboardDataAsync(ClipboardItem item);
}
