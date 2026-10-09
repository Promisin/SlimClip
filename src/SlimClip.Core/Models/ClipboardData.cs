namespace SlimClip.Core.Models;

public class ClipboardData
{
    public ClipboardType Type { get; set; } = ClipboardType.Text;
    public string? Text { get; set; }
    public string? HtmlContent { get; set; }
    public string? RtfContent { get; set; }
    public byte[]? ImageBytes { get; set; }
    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }
    public string[]? FilePaths { get; set; }
}
