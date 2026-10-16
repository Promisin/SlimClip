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
    Task<List<ClipboardItem>> GetPagedItemsAsync(int limit, int offset, ClipboardType? type = null, bool favoriteOnly = false, string? searchText = null, System.Threading.CancellationToken cancellationToken = default);
    Task<(string? HtmlContent, string? RtfContent)> GetItemContentAsync(string id, System.Threading.CancellationToken cancellationToken = default);
}
