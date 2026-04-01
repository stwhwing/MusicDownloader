using System.Collections.ObjectModel;
using System.IO;
using Newtonsoft.Json;

namespace MusicDownloader.Services;

/// <summary>
/// 历史搜索记录服务
/// </summary>
public static class SearchHistoryService
{
    private static readonly string HistoryPath =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "search_history.json");

    private static readonly int MaxHistoryCount = 50;
    private static ObservableCollection<string>? _cachedHistory;

    /// <summary>
    /// 获取历史记录（带缓存）
    /// </summary>
    public static ObservableCollection<string> GetHistory()
    {
        if (_cachedHistory != null)
            return _cachedHistory;

        _cachedHistory = new ObservableCollection<string>();
        try
        {
            if (File.Exists(HistoryPath))
            {
                var json = File.ReadAllText(HistoryPath);
                var list = JsonConvert.DeserializeObject<List<string>>(json);
                if (list != null)
                {
                    foreach (var item in list.Take(MaxHistoryCount))
                        _cachedHistory.Add(item);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SearchHistoryService.GetHistory] 读取历史失败: {ex.Message}");
            _cachedHistory ??= new System.Collections.ObjectModel.ObservableCollection<string>();
        }

        return _cachedHistory;
    }

    /// <summary>
    /// 添加搜索关键词到历史
    /// </summary>
    public static void AddToHistory(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return;

        keyword = keyword.Trim();
        var history = GetHistory();

        // 移除已存在的相同关键词（移到最前面）
        history.Remove(keyword);
        history.Insert(0, keyword);

        // 保持最大数量
        while (history.Count > MaxHistoryCount)
            history.RemoveAt(history.Count - 1);

        SaveHistory(history);
    }

    /// <summary>
    /// 清空历史记录
    /// </summary>
    public static void ClearHistory()
    {
        _cachedHistory?.Clear();
        try
        {
            if (File.Exists(HistoryPath))
                File.Delete(HistoryPath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SearchHistoryService.ClearHistory] 清空历史失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 删除单条历史
    /// </summary>
    public static void RemoveFromHistory(string keyword)
    {
        var history = GetHistory();
        history.Remove(keyword);
        SaveHistory(history);
    }

    private static void SaveHistory(ObservableCollection<string> history)
    {
        try
        {
            var json = JsonConvert.SerializeObject(history.ToList(), Formatting.None);
            File.WriteAllText(HistoryPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SearchHistoryService.SaveHistory] 保存历史失败: {ex.Message}");
        }
    }
}
