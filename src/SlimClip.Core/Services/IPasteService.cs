using System.Threading.Tasks;
using SlimClip.Core.Models;

namespace SlimClip.Core.Services;

public interface IPasteService
{
    Task PasteAsync(ClipboardItem item, int delayMs, bool plainTextOnly = false);
    Task SetClipboardDataAsync(ClipboardItem item, bool plainTextOnly = false);
}
