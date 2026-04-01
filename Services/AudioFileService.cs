using System.IO;
using System.Text.RegularExpressions;
using MusicDownloader.Models;
using TagLib;

namespace MusicDownloader.Services;

/// <summary>
/// 音频文件工具服务：文件扫描、核对、类型识别、重命名、移动到回收站
/// 清洗逻辑委托给 FileNameCleanerService，网易云验证委托给 NeteaseVerificationService
/// </summary>
public class AudioFileService : IDisposable
{
    private static readonly string[] AudioExtensions = { ".mp3", ".flac", ".aac", ".wav", ".ogg", ".m4a" };
    private readonly NeteaseVerificationService _verifier = new();

    // 自适应限速器：初始 300ms，连续成功 5 次后逐步缩短，最少 100ms；失败后重置为 500ms
    private int _rateDelayMs = 300;
    private int _consecutiveSuccesses = 0;
    private bool _disposed = false;
    private readonly object _rateLock = new(); // 保护限速器状态

    // ════════════════════ 文件扫描 ════════════════════

    /// <summary>获取目标路径下所有音频文件</summary>
    public static List<string> GetAudioFiles(string path, bool recursive = false)
    {
        var files = new List<string>();
        if (System.IO.File.Exists(path))
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (AudioExtensions.Contains(ext)) files.Add(path);
        }
        else if (Directory.Exists(path))
        {
            var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            foreach (var ext in AudioExtensions)
                files.AddRange(Directory.GetFiles(path, $"*{ext}", option));
        }
        return files.OrderBy(f => f).ToList();
    }

    // ════════════════════ 文件核对 ════════════════════

    /// <summary>批量核对文件（本地标签对比 + 可选网易云验证）</summary>
    public async Task<List<FileCheckResult>> CheckFilesAsync(
        IEnumerable<string> filePaths,
        bool useOnlineVerification,
        IProgress<(int current, int total, string file)>? progress = null,
        CancellationToken ct = default)
    {
        var list = filePaths.ToList();
        var results = new List<FileCheckResult>();

        for (int i = 0; i < list.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report((i + 1, list.Count, Path.GetFileName(list[i])));

            var result = CheckFileLocal(list[i]);

            if (useOnlineVerification && result.Status != FileCheckStatus.Error)
            {
                var verifyOk = await VerifyWithNeteaseAsync(result, ct);
                // 自适应限速：成功则逐步缩短间隔，失败则延长间隔（线程安全）
                int delayMs;
                lock (_rateLock)
                {
                    if (verifyOk)
                    {
                        _consecutiveSuccesses++;
                        if (_consecutiveSuccesses >= 5 && _rateDelayMs > 100)
                        {
                            _rateDelayMs = Math.Max(100, _rateDelayMs - 50);
                            _consecutiveSuccesses = 0;
                        }
                    }
                    else
                    {
                        _rateDelayMs = 500;
                        _consecutiveSuccesses = 0;
                    }
                    delayMs = _rateDelayMs;
                }
                await Task.Delay(delayMs, ct);
            }

            results.Add(result);
        }
        return results;
    }

    /// <summary>本地标签核对</summary>
    private static FileCheckResult CheckFileLocal(string filePath)
    {
        var result = new FileCheckResult
        {
            FilePath = filePath,
            FileName = Path.GetFileNameWithoutExtension(filePath)
        };

        FileNameCleanerService.ParseFileName(result.FileName, out var nameArtist, out var nameTitle);
        result.NameArtist = nameArtist;
        result.NameTitle = nameTitle;

        try
        {
            using var tagFile = TagLib.File.Create(filePath);
            result.TagArtist = tagFile.Tag.FirstPerformer ?? "";
            result.TagTitle = tagFile.Tag.Title ?? "";

            if (string.IsNullOrEmpty(result.TagArtist) && string.IsNullOrEmpty(result.TagTitle))
            {
                result.Status = FileCheckStatus.NoTag;
                result.Note = "无 ID3 标签";
                return result;
            }

            bool artistMatch = string.IsNullOrEmpty(nameArtist) ||
                               Norm(result.TagArtist).Contains(Norm(nameArtist)) ||
                               Norm(nameArtist).Contains(Norm(result.TagArtist));
            bool titleMatch = string.IsNullOrEmpty(nameTitle) ||
                              Norm(result.TagTitle).Contains(Norm(nameTitle)) ||
                              Norm(nameTitle).Contains(Norm(result.TagTitle));

            result.Status = (artistMatch && titleMatch) ? FileCheckStatus.Match : FileCheckStatus.Mismatch;
            if (result.Status == FileCheckStatus.Mismatch)
            {
                var parts = new List<string>();
                if (!artistMatch) parts.Add($"歌手：文件名「{nameArtist}」vs 标签「{result.TagArtist}」");
                if (!titleMatch) parts.Add($"歌名：文件名「{nameTitle}」vs 标签「{result.TagTitle}」");
                result.Note = string.Join("；", parts);
            }
            else
            {
                result.Note = "本地一致";
            }
        }
        catch (Exception ex)
        {
            result.Status = FileCheckStatus.Error;
            result.Note = $"读取失败: {ex.Message}";
        }

        return result;
    }

    /// <summary>网易云可信源验证（返回验证是否成功执行）</summary>
    private async Task<bool> VerifyWithNeteaseAsync(FileCheckResult result, CancellationToken ct)
    {
        var artist = !string.IsNullOrEmpty(result.TagArtist) ? result.TagArtist : result.NameArtist;
        var title = !string.IsNullOrEmpty(result.TagTitle) ? result.TagTitle : result.NameTitle;
        var keyword = string.IsNullOrEmpty(artist) ? title : $"{artist} {title}";
        if (string.IsNullOrEmpty(keyword)) return true; // 无关键词，视为正常

        var verifyResult = await _verifier.VerifyAsync(keyword, result.Status, ct);

        if (!verifyResult.IsVerified)
        {
            result.IsVerified = false;
            result.Note += verifyResult.Note;
            return false; // 验证失败
        }

        result.VerifiedArtist = verifyResult.VerifiedArtist;
        result.VerifiedTitle = verifyResult.VerifiedTitle;
        result.IsVerified = true;

        var nameOk = Norm(result.FileName).Contains(Norm(result.VerifiedArtist)) ||
                     Norm(result.FileName).Contains(Norm(result.VerifiedTitle));

        if (result.Status == FileCheckStatus.Match && nameOk)
            result.Note = $"✅ 本地一致 + 网易云验证";
        else if (result.Status == FileCheckStatus.Match && !nameOk)
        {
            result.Status = FileCheckStatus.Mismatch;
            result.Note = $"网易云：「{result.VerifiedArtist} - {result.VerifiedTitle}」";
        }
        else if (result.Status == FileCheckStatus.Mismatch || result.Status == FileCheckStatus.NoTag)
        {
            result.Note += $"  |  网易云建议：「{result.VerifiedArtist} - {result.VerifiedTitle}」";
        }
        return true; // 验证成功
    }

    // ════════════════════ 清洗重命名 ════════════════════

    /// <summary>生成清洗重命名计划（不执行，仅预览）</summary>
    public static List<RenameItem> BuildRenamePlan(IEnumerable<string> filePaths)
    {
        var list = filePaths.ToList();
        var plan = new List<RenameItem>();
        var dirTargets = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var filePath in list)
        {
            var dir = Path.GetDirectoryName(filePath) ?? "";
            var ext = Path.GetExtension(filePath);
            var nameNoExt = Path.GetFileNameWithoutExtension(filePath);

            FileNameCleanerService.ParseFileName(nameNoExt, out var artist, out var titlePart);

            var cleanArtist = FileNameCleanerService.Clean(
                string.IsNullOrEmpty(artist) ? nameNoExt : artist);
            var cleanTitle = FileNameCleanerService.Clean(
                string.IsNullOrEmpty(titlePart) ? "" : titlePart);

            string newNameNoExt;
            if (!string.IsNullOrEmpty(cleanArtist) && !string.IsNullOrEmpty(cleanTitle))
                newNameNoExt = $"{cleanArtist} - {cleanTitle}";
            else if (!string.IsNullOrEmpty(cleanArtist))
                newNameNoExt = cleanArtist;
            else
                newNameNoExt = nameNoExt;

            newNameNoExt = FileNameCleanerService.SanitizeFileName(newNameNoExt);

            if (!dirTargets.ContainsKey(dir))
                dirTargets[dir] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var targetSet = dirTargets[dir];
            var candidateName = newNameNoExt + ext;
            var candidateNameLower = candidateName.ToLowerInvariant();

            if (targetSet.Contains(candidateNameLower) &&
                !string.Equals(candidateName, nameNoExt + ext, StringComparison.OrdinalIgnoreCase))
            {
                int counter = 1;
                while (targetSet.Contains($"{newNameNoExt} ({counter}){ext}".ToLowerInvariant()))
                    counter++;
                newNameNoExt = $"{newNameNoExt} ({counter})";
                candidateName = newNameNoExt + ext;
                candidateNameLower = candidateName.ToLowerInvariant();
            }
            targetSet.Add(candidateNameLower);

            var item = new RenameItem
            {
                OriginalPath = filePath,
                OriginalFileName = nameNoExt + ext,
                NewFileName = candidateName,
                WillConflict = System.IO.File.Exists(Path.Combine(dir, candidateName)) &&
                               !string.Equals(filePath, Path.Combine(dir, candidateName), StringComparison.OrdinalIgnoreCase)
            };

            if (string.Equals(item.OriginalFileName, item.NewFileName, StringComparison.OrdinalIgnoreCase))
                item.Status = RenameStatus.Skipped;

            plan.Add(item);
        }

        return plan;
    }

    /// <summary>
    /// 合并清洗重命名计划（可信源验证版）：
    /// 1. 读取文件名 → 解析歌手/歌名
    /// 2. 逐一调用网易云 API 验证（可信源）
    /// 3. 用网易云权威歌手/歌名（如验证通过）生成清洗计划
    /// 4. 未通过验证的文件使用本地解析结果
    /// 支持暂停 CancellationToken
    /// </summary>
    public async Task<List<RenameItem>> BuildVerifiedRenamePlanAsync(
        IEnumerable<string> filePaths,
        bool useOnlineVerify,
        IProgress<(int current, int total, string file, string status)>? progress = null,
        CancellationToken ct = default)
    {
        var list = filePaths.ToList();
        var plan = new List<RenameItem>();
        var dirTargets = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < list.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var filePath = list[i];
            var dir = Path.GetDirectoryName(filePath) ?? "";
            var ext = Path.GetExtension(filePath);
            var nameNoExt = Path.GetFileNameWithoutExtension(filePath);

            FileNameCleanerService.ParseFileName(nameNoExt, out var nameArtist, out var nameTitle);

            var item = new RenameItem
            {
                OriginalPath = filePath,
                OriginalFileName = nameNoExt + ext,
                NameArtist = nameArtist,
                NameTitle = nameTitle,
            };

            progress?.Report((i + 1, list.Count, nameNoExt + ext, "网易云验证中..."));

            // ════════ 可信源验证（网易云） ════════
            if (useOnlineVerify && !string.IsNullOrWhiteSpace(nameArtist + nameTitle))
            {
                var keyword = string.IsNullOrEmpty(nameArtist) ? nameTitle : $"{nameArtist} {nameTitle}";
                var verifyResult = await _verifier.VerifyAsync(keyword, FileCheckStatus.Match, ct);
                item.IsVerified = verifyResult.IsVerified;
                item.VerifiedArtist = verifyResult.VerifiedArtist;
                item.VerifiedTitle = verifyResult.VerifiedTitle;

                // 自适应限速（线程安全：使用实例字段，跨批次持久化）
                int delayMs;
                lock (_rateLock)
                {
                    if (verifyResult.IsVerified)
                    {
                        _consecutiveSuccesses++;
                        if (_consecutiveSuccesses >= 5 && _rateDelayMs > 100)
                        {
                            _rateDelayMs = Math.Max(100, _rateDelayMs - 50);
                            _consecutiveSuccesses = 0;
                        }
                    }
                    else
                    {
                        _rateDelayMs = 500;
                        _consecutiveSuccesses = 0;
                    }
                    delayMs = _rateDelayMs;
                }
                await Task.Delay(delayMs, ct);
            }
            else
            {
                item.IsVerified = false;
            }

            // ════════ 生成目标文件名 ════════
            // 优先使用可信源，其次使用本地解析
            var srcArtist = item.IsVerified ? item.VerifiedArtist : nameArtist;
            var srcTitle = item.IsVerified ? item.VerifiedTitle : nameTitle;

            var cleanArtist = FileNameCleanerService.Clean(srcArtist ?? "");
            var cleanTitle = FileNameCleanerService.Clean(srcTitle ?? "");
            if (!string.IsNullOrEmpty(cleanTitle))
                cleanTitle = FileNameCleanerService.RemoveTitleParenRepeat(cleanTitle, cleanArtist ?? "");

            string newNameNoExt;
            if (!string.IsNullOrEmpty(cleanArtist) && !string.IsNullOrEmpty(cleanTitle))
                newNameNoExt = $"{cleanArtist} - {cleanTitle}";
            else if (!string.IsNullOrEmpty(cleanArtist))
                newNameNoExt = cleanArtist;
            else
                newNameNoExt = nameNoExt;

            newNameNoExt = FileNameCleanerService.SanitizeFileName(newNameNoExt);

            if (!dirTargets.ContainsKey(dir))
                dirTargets[dir] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var targetSet = dirTargets[dir];
            var candidateName = newNameNoExt + ext;
            var candidateNameLower = candidateName.ToLowerInvariant();

            if (targetSet.Contains(candidateNameLower) &&
                !string.Equals(candidateName, nameNoExt + ext, StringComparison.OrdinalIgnoreCase))
            {
                int counter = 1;
                while (targetSet.Contains($"{newNameNoExt} ({counter}){ext}".ToLowerInvariant()))
                    counter++;
                newNameNoExt = $"{newNameNoExt} ({counter})";
                candidateName = newNameNoExt + ext;
                candidateNameLower = candidateName.ToLowerInvariant();
            }
            targetSet.Add(candidateNameLower);

            item.NewFileName = candidateName;
            item.WillConflict = System.IO.File.Exists(Path.Combine(dir, candidateName)) &&
                               !string.Equals(filePath, Path.Combine(dir, candidateName), StringComparison.OrdinalIgnoreCase);

            if (string.Equals(item.OriginalFileName, item.NewFileName, StringComparison.OrdinalIgnoreCase))
            {
                item.Status = RenameStatus.Skipped;
                item.Note = item.IsVerified ? "✅ 已验证，无需改名" : "✅ 文件名已规范，无需改名";
            }
            else if (item.IsVerified)
            {
                item.Note = "✅ 网易云已验证";
            }
            else if (!string.IsNullOrWhiteSpace(nameArtist + nameTitle))
            {
                item.Note = "⚠️ 可信源未找到（使用本地解析）";
            }
            else
            {
                item.Note = "⚠️ 无歌手/歌名信息";
            }

            progress?.Report((i + 1, list.Count, nameNoExt + ext, item.Note));
            plan.Add(item);
        }

        return plan;
    }

    /// <summary>执行重命名计划（支持 CancellationToken 取消）</summary>
    public static void ExecuteRenamePlan(List<RenameItem> plan,
        IProgress<(int current, int total, string file)>? progress = null,
        CancellationToken ct = default)
    {
        int total = plan.Count(p => p.Status == RenameStatus.Pending);
        int current = 0;

        foreach (var item in plan)
        {
            ct.ThrowIfCancellationRequested();
            if (item.Status != RenameStatus.Pending) continue;

            current++;
            progress?.Report((current, total, item.OriginalFileName));

            try
            {
                if (System.IO.File.Exists(item.NewPath) &&
                    !string.Equals(item.OriginalPath, item.NewPath, StringComparison.OrdinalIgnoreCase))
                {
                    item.Status = RenameStatus.Failed;
                    item.Note = "目标文件已存在，跳过";
                    continue;
                }

                System.IO.File.Move(item.OriginalPath, item.NewPath);
                item.Status = RenameStatus.Done;
            }
            catch (Exception ex)
            {
                item.Status = RenameStatus.Failed;
                item.Note = ex.Message;
            }
        }
    }

    // ════════════════════ Cover/DJ/AI 识别与删除 ════════════════════

    private static readonly Regex ReCover = new(
        @"cover|翻唱|翻唱版|演唱版|Cover版|翻奏",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ReDj = new(
        @"\bDJ\b|dj版|Dj版|DJ版|club\s*mix|dance\s*mix|remix|电音|舞曲|劲爆版|超劲爆",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ReAi = new(
        @"\bAI\b|AI版|ai翻唱|人工智能",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>识别文件类型（Cover/DJ/AI）</summary>
    public static FileTypeResult DetectFileType(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        var result = new FileTypeResult { FilePath = filePath, FileName = name };

        var coverMatch = ReCover.Match(name);
        var djMatch = ReDj.Match(name);
        var aiMatch = ReAi.Match(name);

        result.IsCover = coverMatch.Success;
        result.IsDj = djMatch.Success;
        result.IsAi = aiMatch.Success;

        if (coverMatch.Success) result.MatchedKeyword += $"Cover({coverMatch.Value}) ";
        if (djMatch.Success) result.MatchedKeyword += $"DJ({djMatch.Value}) ";
        if (aiMatch.Success) result.MatchedKeyword += $"AI({aiMatch.Value}) ";
        result.MatchedKeyword = result.MatchedKeyword.Trim();

        return result;
    }

    /// <summary>批量检测 Cover/DJ/AI 文件</summary>
    public static List<FileTypeResult> DetectFileTypes(IEnumerable<string> filePaths)
        => filePaths.Select(DetectFileType)
                    .Where(r => r.IsCover || r.IsDj || r.IsAi)
                    .ToList();

    /// <summary>将文件移入回收站（安全删除）</summary>
    public static (int success, int fail, List<string> errors) MoveToRecycleBin(
        IEnumerable<string> filePaths,
        IProgress<(int current, int total, string file)>? progress = null)
    {
        var list = filePaths.ToList();
        int success = 0, fail = 0;
        var errors = new List<string>();

        for (int i = 0; i < list.Count; i++)
        {
            progress?.Report((i + 1, list.Count, Path.GetFileName(list[i])));
            try
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                    list[i],
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                success++;
            }
            catch (Exception ex)
            {
                fail++;
                errors.Add($"{Path.GetFileName(list[i])}: {ex.Message}");
            }
        }
        return (success, fail, errors);
    }

    // ════════════════════ 私有工具 ════════════════════

    private static string Norm(string s) => s.Trim().ToLowerInvariant();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; // 先置标志，防止异常导致重复 Dispose
        _verifier?.Dispose();
    }
}

// ════════════════════ 数据模型 ════════════════════

/// <summary>音频文件核对结果</summary>
public class FileCheckResult
{
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public string TagArtist { get; set; } = "";
    public string TagTitle { get; set; } = "";
    public string NameArtist { get; set; } = "";
    public string NameTitle { get; set; } = "";
    /// <summary>网易云权威歌手</summary>
    public string VerifiedArtist { get; set; } = "";
    /// <summary>网易云权威歌名</summary>
    public string VerifiedTitle { get; set; } = "";
    public FileCheckStatus Status { get; set; }
    public string Note { get; set; } = "";
    /// <summary>是否已通过可信源验证</summary>
    public bool IsVerified { get; set; }
}

public enum FileCheckStatus
{
    Match,       // ✅ 一致
    Mismatch,    // ⚠️ 不一致
    NoTag,       // ❓ 无标签
    NotFound,    // 🔍 可信源未找到
    Error        // ❌ 读取错误
}

/// <summary>文件重命名计划条目</summary>
public class RenameItem : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public string OriginalPath { get; set; } = "";
    public string OriginalFileName { get; set; } = "";

    private string _newFileName = "";
    public string NewFileName
    {
        get => _newFileName;
        set
        {
            if (SetProperty(ref _newFileName, value))
                OnPropertyChanged(nameof(NewPath));
        }
    }

    public string NewPath => Path.Combine(Path.GetDirectoryName(OriginalPath) ?? "", NewFileName);
    public bool WillConflict { get; set; }
    public RenameStatus Status { get; set; } = RenameStatus.Pending;
    public string Note { get; set; } = "";

    // ═══════ 可信源验证信息（网易云） ═══════
    /// <summary>网易云权威歌手（可信源）</summary>
    public string VerifiedArtist { get; set; } = "";
    /// <summary>网易云权威歌名（可信源）</summary>
    public string VerifiedTitle { get; set; } = "";
    /// <summary>是否已通过网易云可信源验证</summary>
    public bool IsVerified { get; set; }
    /// <summary>文件名解析出的歌手（仅供参考）</summary>
    public string NameArtist { get; set; } = "";
    /// <summary>文件名解析出的歌名（仅供参考）</summary>
    public string NameTitle { get; set; } = "";
}

public enum RenameStatus
{
    Pending,   // 待执行
    Done,      // 已完成
    Skipped,   // 跳过（文件名未变）
    Failed     // 失败
}

/// <summary>Cover/DJ/AI 类型识别结果</summary>
public class FileTypeResult
{
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public bool IsCover { get; set; }
    public bool IsDj { get; set; }
    public bool IsAi { get; set; }
    public string MatchedKeyword { get; set; } = "";
}
