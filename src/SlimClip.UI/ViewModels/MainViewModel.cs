using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SlimClip.Core.Models;
using SlimClip.Core.Services;

namespace SlimClip.UI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IClipboardHistory _history;
    private readonly IClipboardMonitor _monitor;
    private readonly IPasteService _pasteService;
    private readonly ISettingsService _settingsService;
    private readonly ILocalizationService _localizationService;
    private readonly System.Threading.SynchronizationContext? _syncContext = System.Threading.SynchronizationContext.Current;
    public const int PageSize = 50;
    private int _currentOffset = 0;
    private CancellationTokenSource? _searchCts;

    private void RunOnUiContext(Action action)
    {
        if (_syncContext != null && System.Threading.SynchronizationContext.Current != _syncContext)
        {
            _syncContext.Post(_ => action(), null);
        }
        else
        {
            action();
        }
    }

    public int SearchDebounceMs { get; set; } = 0;
    public event Action? RequestScrollToHome;

    public ILocalizationService L => _localizationService;

    public event Func<string, Task<bool>>? ConfirmMissingFiles;

    [ObservableProperty]
    private ObservableCollection<ClipboardItem> _items = new();

    [ObservableProperty]
    private ClipboardItem? _selectedItem;

    [ObservableProperty]
    private bool _isLoadingMore;

    [ObservableProperty]
    private bool _hasMoreItems = true;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _currentFilterType = "All";

    [ObservableProperty]
    private bool _hasItems;

    [ObservableProperty]
    private bool _isEditingItem;

    [ObservableProperty]
    private ClipboardItem? _editingTargetItem;

    [ObservableProperty]
    private string _editingText = string.Empty;

    [ObservableProperty]
    private bool _hasRichText;

    [ObservableProperty]
    private bool _isRichTextMode;

    private string _bufferedPlainText = string.Empty;
    private string _bufferedHtmlContent = string.Empty;

    public string CharCountDisplay => _localizationService.GetString("Editor_CharCount", EditingText?.Length ?? 0);

    public string EmptyTitleDisplay
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(SearchText))
                return _localizationService["Panel_Empty_Search"];

            return CurrentFilterType switch
            {
                "Favorite" => _localizationService["Panel_Empty_Favorite"],
                "Text" => _localizationService["Panel_Empty_Text"],
                "Image" => _localizationService["Panel_Empty_Image"],
                "File" => _localizationService["Panel_Empty_File"],
                _ => _localizationService["Panel_Empty_All"]
            };
        }
    }

    public string EmptyHintDisplay
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(SearchText))
                return _localizationService["Panel_Empty_Search_Hint"];

            return CurrentFilterType switch
            {
                "Favorite" => _localizationService["Panel_Empty_Favorite_Hint"],
                "Text" => _localizationService["Panel_Empty_Text_Hint"],
                "Image" => _localizationService["Panel_Empty_Image_Hint"],
                "File" => _localizationService["Panel_Empty_File_Hint"],
                _ => _localizationService["Panel_Empty_All_Hint"]
            };
        }
    }

    public string EmptyIconKind
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(SearchText))
                return "search";

            return CurrentFilterType switch
            {
                "Favorite" => "star",
                "Text" => "text",
                "Image" => "image",
                "File" => "file",
                _ => "search"
            };
        }
    }

    partial void OnEditingTextChanged(string value)
    {
        OnPropertyChanged(nameof(CharCountDisplay));
    }

    public MainViewModel(
        IClipboardHistory history,
        IClipboardMonitor monitor,
        IPasteService pasteService,
        ISettingsService settingsService,
        ILocalizationService? localizationService = null)
    {
        _history = history;
        _monitor = monitor;
        _pasteService = pasteService;
        _settingsService = settingsService;
        _localizationService = localizationService ?? new LocalizationService(_settingsService.Settings.Language);

        _localizationService.LanguageChanged += OnLanguageChanged;
        UpdateDateFormatter();

        _monitor.DataCopied += OnDataCopied;
    }

    private void OnLanguageChanged(object? sender, string lang)
    {
        UpdateDateFormatter();
        OnPropertyChanged(string.Empty);
        OnPropertyChanged(nameof(L));
        OnPropertyChanged(nameof(CharCountDisplay));
        OnPropertyChanged(nameof(EmptyTitleDisplay));
        OnPropertyChanged(nameof(EmptyHintDisplay));
    }

    private void UpdateDateFormatter()
    {
        var todayStr = _localizationService["Time_Today"];
        var yesterdayStr = _localizationService["Time_Yesterday"];
        ClipboardItem.DateFormatter = dt =>
        {
            var now = DateTime.Now;
            if (dt.Date == now.Date)
                return $"{todayStr} {dt:HH:mm:ss}";
            if (dt.Date == now.AddDays(-1).Date)
                return $"{yesterdayStr} {dt:HH:mm:ss}";
            return dt.ToString("yyyy-MM-dd HH:mm:ss");
        };

        foreach (var item in Items)
        {
            item.NotifyCopiedAtChanged();
        }
    }

    public async Task InitializeAsync()
    {
        if (_settingsService.Settings.StartEmptyHistory)
        {
            await _history.ClearAllAsync();
        }
        else if (_settingsService.Settings.PeriodicCleanEnabled && _settingsService.Settings.CleanPeriodDays > 0)
        {
            await _history.CleanPeriodicAsync(_settingsService.Settings.CleanPeriodDays);
        }

        await RefreshFilteredListAsync();
    }

    partial void OnSearchTextChanged(string value)
    {
        if (SearchDebounceMs <= 0)
        {
            RefreshFilteredList();
        }
        else
        {
            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var token = _searchCts.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(SearchDebounceMs, token);
                    if (!token.IsCancellationRequested)
                    {
                        RunOnUiContext(() =>
                        {
                            if (!token.IsCancellationRequested)
                            {
                                _ = RefreshFilteredListAsync(token);
                            }
                        });
                    }
                }
                catch (OperationCanceledException) { }
            }, token);
        }
    }

    partial void OnCurrentFilterTypeChanged(string value)
    {
        RefreshFilteredList();
    }

    private async void OnDataCopied(object? sender, ClipboardData data)
    {
        try
        {
            var recorded = await _history.RecordAsync(data);

            if (_settingsService.Settings.AutoCleanEnabled && _settingsService.Settings.MaxHistoryItems > 0)
            {
                await _history.TrimExcessAsync(_settingsService.Settings.MaxHistoryItems);
            }

            var (type, favoriteOnly, search) = GetQueryFilter();
            bool matchesType = !type.HasValue || recorded.Type == type.Value;
            bool matchesFav = !favoriteOnly || recorded.IsFavorite;
            bool matchesSearch = string.IsNullOrWhiteSpace(search) || 
                                 (!string.IsNullOrEmpty(recorded.Text) && recorded.Text.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                                 (!string.IsNullOrEmpty(recorded.FileDisplayName) && recorded.FileDisplayName.Contains(search, StringComparison.OrdinalIgnoreCase));

            if (matchesType && matchesFav && matchesSearch)
            {
                var existingIndex = -1;
                for (int i = 0; i < Items.Count; i++)
                {
                    if (Items[i].Id == recorded.Id)
                    {
                        existingIndex = i;
                        break;
                    }
                }

                if (existingIndex >= 0)
                {
                    Items.Move(existingIndex, 0);
                    Items[0] = recorded;
                }
                else
                {
                    Items.Insert(0, recorded);
                }

                HasItems = Items.Count > 0;
                SelectedItem = Items.FirstOrDefault();
                UpdateEmptyDisplays();
                RequestScrollToHome?.Invoke();
            }
            else
            {
                RefreshFilteredList();
            }
        }
        catch
        {
        }
    }

    public void ResetState()
    {
        SearchText = string.Empty;
        CurrentFilterType = "All";
        RefreshFilteredList();
        SelectedItem = Items.FirstOrDefault();
    }

    public void RefreshFilteredList()
    {
        _ = RefreshFilteredListAsync();
    }

    public async Task RefreshFilteredListAsync(CancellationToken ct = default)
    {
        _currentOffset = 0;
        HasMoreItems = true;

        var (type, favoriteOnly, search) = GetQueryFilter();
        var paged = await _history.GetPagedItemsAsync(PageSize, 0, type, favoriteOnly, search, ct);

        Items = new ObservableCollection<ClipboardItem>(paged);
        _currentOffset = paged.Count;
        HasMoreItems = paged.Count >= PageSize;
        HasItems = Items.Count > 0;
        SelectedItem = Items.FirstOrDefault();
        UpdateEmptyDisplays();
        RequestScrollToHome?.Invoke();
    }

    private (ClipboardType? type, bool favoriteOnly, string? search) GetQueryFilter()
    {
        bool favoriteOnly = CurrentFilterType == "Favorite";
        ClipboardType? type = CurrentFilterType switch
        {
            "Text" => ClipboardType.Text,
            "Image" => ClipboardType.Image,
            "File" => ClipboardType.File,
            _ => null
        };
        string? search = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim();
        return (type, favoriteOnly, search);
    }

    [RelayCommand]
    public async Task LoadNextPageAsync()
    {
        if (IsLoadingMore || !HasMoreItems)
            return;

        IsLoadingMore = true;
        try
        {
            var (type, favoriteOnly, search) = GetQueryFilter();
            var paged = await _history.GetPagedItemsAsync(PageSize, _currentOffset, type, favoriteOnly, search);
            if (paged.Count > 0)
            {
                foreach (var item in paged)
                {
                    Items.Add(item);
                }
                _currentOffset += paged.Count;
            }

            if (paged.Count < PageSize)
            {
                HasMoreItems = false;
            }
        }
        finally
        {
            IsLoadingMore = false;
        }
    }

    private void UpdateEmptyDisplays()
    {
        OnPropertyChanged(nameof(EmptyTitleDisplay));
        OnPropertyChanged(nameof(EmptyHintDisplay));
        OnPropertyChanged(nameof(EmptyIconKind));
    }

    private async Task EnsureItemContentAsync(ClipboardItem item)
    {
        if (item.Type == ClipboardType.Text && item.HtmlContent == null && item.RtfContent == null)
        {
            var (html, rtf) = await _history.GetItemContentAsync(item.Id);
            item.HtmlContent = html;
            item.RtfContent = rtf;
        }
    }

    [RelayCommand]
    public async Task PasteSelectedAsync()
    {
        if (SelectedItem == null)
            return;

        var item = SelectedItem;
        await EnsureItemContentAsync(item);

        // Check if file type has missing files
        if (item.IsFile)
        {
            var paths = item.ParsedFilePaths;
            var (existing, missing) = FileExistenceValidator.Partition(paths);
            if (missing.Count > 0)
            {
                if (existing.Count == 0)
                {
                    if (ConfirmMissingFiles != null)
                        await ConfirmMissingFiles("剪贴板中的源文件已被移动或删除，无法粘贴。");
                    return;
                }

                if (ConfirmMissingFiles != null)
                {
                    var proceed = await ConfirmMissingFiles($"部分源文件已被移动或删除：\n{string.Join('\n', missing.Take(3))}\n是否仅粘贴其余 {existing.Count} 个有效文件？");
                    if (!proceed)
                        return;
                }
            }
        }

        _monitor.SuppressNextCapture();
        var delay = _settingsService.Settings.PasteDelayMs;
        await _pasteService.PasteAsync(item, delay);
        await _history.UpdateTimestampAsync(item.Id, DateTime.Now);
    }

    private void RemoveItemAndSelectFallback(ClipboardItem item)
    {
        Items.Remove(item);
        HasItems = Items.Count > 0;
        SelectedItem = Items.FirstOrDefault();
        UpdateEmptyDisplays();
    }

    [RelayCommand]
    public async Task ToggleFavoriteAsync(ClipboardItem? item)
    {
        if (item == null)
            return;

        await _history.ToggleFavoriteAsync(item.Id);
        item.IsFavorite = !item.IsFavorite;
        if (CurrentFilterType == "Favorite" && !item.IsFavorite)
        {
            RemoveItemAndSelectFallback(item);
        }
    }

    [RelayCommand]
    public async Task DeleteItemAsync(ClipboardItem? item)
    {
        if (item == null)
            return;

        await _history.DeleteItemAsync(item.Id);
        RemoveItemAndSelectFallback(item);
    }

    public void CycleFilter(bool forward)
    {
        string[] categories = ["Favorite", "All", "Text", "Image", "File"];
        int idx = Array.IndexOf(categories, CurrentFilterType);
        if (idx < 0) idx = 1;
        int nextIdx = forward
            ? Math.Min(idx + 1, categories.Length - 1)
            : Math.Max(idx - 1, 0);
        CurrentFilterType = categories[nextIdx];
    }

    [RelayCommand]
    public async Task CopyItemToClipboardAsync(ClipboardItem? item)
    {
        item ??= SelectedItem;
        if (item == null)
            return;

        await EnsureItemContentAsync(item);

        _monitor.SuppressNextCapture();
        await _pasteService.SetClipboardDataAsync(item);
        var now = DateTime.Now;
        await _history.UpdateTimestampAsync(item.Id, now);
        item.CopiedAt = now;
        await RefreshFilteredListAsync();
        SelectedItem = Items.FirstOrDefault(i => i.Id == item.Id) ?? item;
    }

    [RelayCommand]
    public async Task StartEditItemAsync(ClipboardItem? item)
    {
        item ??= SelectedItem;
        if (item == null || item.Type != ClipboardType.Text)
            return;

        await EnsureItemContentAsync(item);

        EditingTargetItem = item;
        _bufferedPlainText = item.Text ?? string.Empty;
        _bufferedHtmlContent = item.HtmlContent ?? string.Empty;
        HasRichText = !string.IsNullOrEmpty(item.HtmlContent);
        IsRichTextMode = HasRichText;
        EditingText = IsRichTextMode ? _bufferedHtmlContent : _bufferedPlainText;
        IsEditingItem = true;
    }

    public void StartEditItem(ClipboardItem? item)
    {
        _ = StartEditItemAsync(item);
    }

    [RelayCommand]
    public void SwitchEditMode(bool toRichText)
    {
        if (!HasRichText || IsRichTextMode == toRichText)
            return;

        if (toRichText)
        {
            _bufferedPlainText = EditingText;
            IsRichTextMode = true;
            EditingText = _bufferedHtmlContent;
        }
        else
        {
            _bufferedHtmlContent = EditingText;
            if (_bufferedPlainText == EditingTargetItem?.Text && _bufferedHtmlContent != EditingTargetItem?.HtmlContent)
            {
                var stripped = StripHtmlTags(_bufferedHtmlContent);
                if (!string.IsNullOrWhiteSpace(stripped))
                {
                    _bufferedPlainText = stripped;
                }
            }
            IsRichTextMode = false;
            EditingText = _bufferedPlainText;
        }
    }

    [RelayCommand]
    public async Task SaveEditItemAsync()
    {
        if (EditingTargetItem == null)
        {
            IsEditingItem = false;
            return;
        }

        var target = EditingTargetItem;

        string finalText;
        string? finalHtml;
        string? finalRtf;

        if (HasRichText && IsRichTextMode)
        {
            _bufferedHtmlContent = EditingText;
            finalHtml = _bufferedHtmlContent;
            finalRtf = target.RtfContent;
            if (_bufferedPlainText == target.Text && finalHtml != target.HtmlContent)
            {
                var stripped = StripHtmlTags(finalHtml);
                finalText = !string.IsNullOrWhiteSpace(stripped) ? stripped : _bufferedPlainText;
            }
            else
            {
                finalText = _bufferedPlainText;
            }
        }
        else
        {
            // 纯文本模式下保存：去除掉原有的 HTML 与 RTF 富文本内容，使条目彻底转换为纯文本剪贴项
            finalText = EditingText;
            finalHtml = null;
            finalRtf = null;
        }

        var now = DateTime.Now;
        await _history.UpdateItemContentAsync(target.Id, finalText, finalHtml, finalRtf, now);

        target.Text = finalText;
        target.Preview = ClipboardItem.BuildPreview(finalText);
        target.HtmlContent = finalHtml;
        target.RtfContent = finalRtf;
        target.CopiedAt = now;

        _monitor.SuppressNextCapture();
        await _pasteService.SetClipboardDataAsync(target);

        RefreshFilteredList();
        SelectedItem = target;
        IsEditingItem = false;
        EditingTargetItem = null;
        EditingText = string.Empty;
    }

    [RelayCommand]
    public void CancelEditItem()
    {
        IsEditingItem = false;
        EditingTargetItem = null;
        EditingText = string.Empty;
    }

    private static string StripHtmlTags(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var text = Regex.Replace(html, @"<(br|p|div)[\s/]*>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<[^>]+>", string.Empty);
        text = System.Net.WebUtility.HtmlDecode(text);
        return text.Trim();
    }
}
