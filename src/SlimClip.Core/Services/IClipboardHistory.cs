using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SlimClip.Core.Models;

namespace SlimClip.Core.Services;

public interface IClipboardHistory : IDisposable
{
    Task InitializeAsync();
    Task<ClipboardItem> RecordAsync(ClipboardData data);
    Task<List<ClipboardItem>> GetAllItemsAsync();
    Task<bool> DeleteItemAsync(string id);
    Task<bool> ToggleFavoriteAsync(string id);
    Task ClearAllAsync();
    Task TrimExcessAsync(int maxItems);
    Task CleanPeriodicAsync(int days);
    Task UpdateTimestampAsync(string id, DateTime timestamp);
    Task<bool> UpdateItemContentAsync(string id, string text, string? htmlContent, string? rtfContent, DateTime updatedAt);
}
