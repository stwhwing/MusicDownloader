using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using MusicDownloader.Services;

namespace MusicDownloader.ViewModels;

public partial class FileToolViewModel : ObservableObject, IDisposable
{
    private readonly DialogService _dialogService = new();
    private readonly AudioFileService _audioService = new();
    private CancellationTokenSource? _opCts; // 统一操作取消令牌（支持暂停/取消）
    private bool _disposed = false;

    [ObservableProperty] private string _targetPath = "";
    [ObservableProperty] private bool _recursive = false;
    [ObservableProperty] private bool _isWorking = false;
    [ObservableProperty] private string _toolStatusText = "就绪";

    [ObservableProperty] private bool _useOnlineVerify = true;

    [ObservableProperty] private bool _deleteCover = true;
    [ObservableProperty] private bool _deleteDj = true;
    [ObservableProperty] private bool _deleteAi = true;

    // 暂停控制
    [ObservableProperty] private bool _isOperationRunning = false;
    [ObservableProperty] private bool _canPause = false;

    [RelayCommand]
    private void BrowsePath()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择要处理的文件夹（也可直接在输入框输入文件路径）",
            Multiselect = false
        };
        if (!string.IsNullOrEmpty(TargetPath) && Directory.Exists(TargetPath))
            dialog.InitialDirectory = TargetPath;

        if (dialog.ShowDialog() == true)
            TargetPath = dialog.FolderName;
    }

    /// <summary>
    /// 合并操作：扫描文件 → 网易云可信源验证 → 生成清洗重命名计划 → 预览确认 → 执行
    /// 合并了原有的"核对标签与文件名"和"清洗并重命名"两个功能，
    /// 并以网易云为可信源修正错误的歌手/歌名信息。
    /// 支持暂停（Cancel）。
    /// </summary>
    [RelayCommand]
    private async Task ScanAndRenameAsync()
    {
        if (!ValidatePath()) return;
        if (IsWorking) return; // 防重入

        IsWorking = true;
        IsOperationRunning = true;
        CanPause = true;
        ToolStatusText = "正在扫描文件...";
        _opCts = new CancellationTokenSource();

        try
        {
            var files = await Task.Run(
                () => AudioFileService.GetAudioFiles(TargetPath, Recursive),
                _opCts.Token);

            if (files.Count == 0)
            {
                _dialogService.ShowInfo("未找到任何音频文件（支持 mp3/flac/aac/wav/ogg/m4a）", "核对与重命名");
                ToolStatusText = "未找到音频文件";
                return;
            }

            ToolStatusText = UseOnlineVerify
                ? $"正在核对 {files.Count} 个文件（含网易云验证，每首约 0.3s）..."
                : $"正在核对 {files.Count} 个文件（仅本地解析）...";

            var progress = new Progress<(int current, int total, string file, string status)>(p =>
                ToolStatusText = $"核对中 ({p.current}/{p.total}): {p.file}");

            var plan = await _audioService.BuildVerifiedRenamePlanAsync(
                files, UseOnlineVerify, progress, _opCts.Token);

            var toRename = plan.Count(p => p.Status == RenameStatus.Pending);
            if (toRename == 0)
            {
                _dialogService.ShowInfo(
                    $"共 {plan.Count} 个文件\n✅ 已全部规范，无需重命名。",
                    "重命名结果");
                ToolStatusText = "所有文件名均已规范";
                return;
            }

            if (!_dialogService.ShowRenamePreview(plan, System.Windows.Application.Current.MainWindow))
            {
                ToolStatusText = "已取消";
                return;
            }

            // 预览确认后进入重命名阶段，可暂停
            IsOperationRunning = false;
            CanPause = true;
            await ExecuteRenameAsync(plan, _opCts.Token);
        }
        catch (OperationCanceledException)
        {
            // 区分来源：_opCts 在 ScanAndRenameAsync 中创建，只可能来自扫描/重命名阶段
            ToolStatusText = IsOperationRunning ? "核对阶段已取消" : "重命名阶段已取消";
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"处理出错：\n{ex.Message}", "错误");
            ToolStatusText = $"出错: {ex.Message}";
        }
        finally
        {
            IsWorking = false;
            IsOperationRunning = false;
            CanPause = false;
            _opCts?.Dispose();
            _opCts = null;
        }
    }

    /// <summary>暂停（请求取消）当前操作</summary>
    [RelayCommand]
    private void PauseOperation()
    {
        if (_opCts != null && !_opCts.IsCancellationRequested)
        {
            _opCts.Cancel();
            ToolStatusText = "正在停止...";
        }
    }

    [RelayCommand]
    private async Task DeleteTypeFilesAsync()
    {
        if (!ValidatePath()) return;
        if (IsWorking) return; // 防重入
        if (!DeleteCover && !DeleteDj && !DeleteAi)
        {
            _dialogService.ShowWarning("请至少勾选一个要删除的类型（Cover / DJ / AI）。", "提示");
            return;
        }

        IsWorking = true;
        ToolStatusText = "正在扫描文件类型...";
        List<FileTypeResult> detected;

        try
        {
            var files = await Task.Run(() => AudioFileService.GetAudioFiles(TargetPath, Recursive));
            if (files.Count == 0)
            {
                _dialogService.ShowInfo("未找到任何音频文件。", "提示");
                ToolStatusText = "未找到音频文件";
                return;
            }

            detected = await Task.Run(() => AudioFileService.DetectFileTypes(files));
            detected = detected.Where(r =>
                (DeleteCover && r.IsCover) ||
                (DeleteDj && r.IsDj) ||
                (DeleteAi && r.IsAi)).ToList();
        }
        catch (Exception ex)
        {
            _dialogService.ShowError($"扫描文件时出错：\n{ex.Message}", "错误");
            ToolStatusText = $"出错: {ex.Message}";
            return;
        }
        finally
        {
            IsWorking = false;
        }

        if (detected.Count == 0)
        {
            _dialogService.ShowInfo("未找到符合条件的 Cover/DJ/AI 文件。", "提示");
            ToolStatusText = "无需删除的文件";
            return;
        }

        var rows = detected.Select((r, i) => new ReportRow
        {
            Index = i + 1,
            FileName = r.FileName,
            SubText = r.MatchedKeyword,
            Note = (r.IsCover ? "Cover " : "") + (r.IsDj ? "DJ版 " : "") + (r.IsAi ? "AI版" : ""),
            NoteColor = r.IsDj ? StatusColors.Mismatch : r.IsAi ? "#8E44AD" : "#2980B9"
        }).ToList();

        var confirmed = await _dialogService.ShowConfirmWithButtonAsync(
            "🗑️ 拟删除文件列表（移入回收站）",
            $"共检测到 {detected.Count} 个文件  |  点击确认后开始删除",
            new List<SummaryItem>
            {
                new() { Label = "拟删除", Count = detected.Count, Color = StatusColors.Mismatch },
                new() { Label = "Cover", Count = detected.Count(r => r.IsCover), Color = "#2980B9" },
                new() { Label = "DJ版", Count = detected.Count(r => r.IsDj), Color = StatusColors.Mismatch },
                new() { Label = "AI版", Count = detected.Count(r => r.IsAi), Color = "#8E44AD" },
            },
            rows,
            "🗑️ 确认删除（移入回收站）",
            async () =>
            {
                try
                {
                    IsWorking = true;
                    ToolStatusText = "正在移入回收站...";

                    var filesToDelete = detected.Select(r => r.FilePath).ToList();
                    var prog = new Progress<(int current, int total, string file)>(p =>
                        ToolStatusText = $"删除中 ({p.current}/{p.total}): {p.file}");
                    var (success, fail, errors) = await Task.Run(
                        () => AudioFileService.MoveToRecycleBin(filesToDelete, prog));

                    IsWorking = false;
                    ToolStatusText = $"删除完成: 成功 {success}，失败 {fail}";

                    var resultRows = detected.Select((r, i) => new ReportRow
                    {
                        Index = i + 1,
                        FileName = r.FileName,
                        SubText = r.MatchedKeyword,
                        // errors 格式为 "文件名（含扩展名）: 错误信息"，用完整文件名精确匹配
                        Note = errors.Any(e =>
                        {
                            var colonIdx = e.IndexOf(':');
                            return colonIdx > 0 &&
                                   string.Equals(e.Substring(0, colonIdx).Trim(),
                                                 System.IO.Path.GetFileName(r.FilePath),
                                                 StringComparison.OrdinalIgnoreCase);
                        }) ? "❌ 失败" : "✅ 已移入回收站",
                        NoteColor = errors.Any(e =>
                        {
                            var colonIdx = e.IndexOf(':');
                            return colonIdx > 0 &&
                                   string.Equals(e.Substring(0, colonIdx).Trim(),
                                                 System.IO.Path.GetFileName(r.FilePath),
                                                 StringComparison.OrdinalIgnoreCase);
                        }) ? StatusColors.Error : StatusColors.Done
                    }).ToList();

                    _dialogService.ShowReport(
                        "🗑️ 删除完成报告",
                        $"目标: {TargetPath}  |  完成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                        new List<SummaryItem>
                        {
                            new() { Label = "删除成功", Count = success, Color = StatusColors.Done },
                            new() { Label = "失败", Count = fail, Color = fail > 0 ? StatusColors.Error : "#606070" },
                        },
                        resultRows,
                        System.Windows.Application.Current.MainWindow);
                }
                catch (Exception ex)
                {
                    ToolStatusText = $"删除出错: {ex.Message}";
                }
                finally
                {
                    IsWorking = false;
                }
            },
            System.Windows.Application.Current.MainWindow);

        if (!confirmed) { ToolStatusText = "已取消删除"; }
    }

    private async Task ExecuteRenameAsync(List<RenameItem> plan, CancellationToken ct)
    {
        IsWorking = true;
        ToolStatusText = "正在重命名...";
        bool wasCancelled = false;

        try
        {
            var progress = new Progress<(int current, int total, string file)>(p =>
                ToolStatusText = $"重命名 ({p.current}/{p.total}): {p.file}");
            await Task.Run(() => AudioFileService.ExecuteRenamePlan(plan, progress, ct));
        }
        catch (OperationCanceledException)
        {
            wasCancelled = true;
            ToolStatusText = "重命名阶段已取消";
            return; // 直接返回，不显示完成报告
        }
        finally { IsWorking = false; IsOperationRunning = false; CanPause = false; }

        if (wasCancelled) return; // 双重保护（理论上不会到达）

        var done = plan.Count(p => p.Status == RenameStatus.Done);
        var failed = plan.Count(p => p.Status == RenameStatus.Failed);
        var skipped = plan.Count(p => p.Status == RenameStatus.Skipped);
        ToolStatusText = $"重命名完成: 成功 {done}，失败 {failed}，跳过 {skipped}";

        var resultRows = plan.Select((item, i) => new ReportRow
        {
            Index = i + 1,
            FileName = item.OriginalFileName,
            SubText = item.Status == RenameStatus.Done ? $"→ {item.NewFileName}" : "",
            Note = item.Status switch
            {
                RenameStatus.Done => "✅ 已完成",
                RenameStatus.Skipped => "跳过",
                RenameStatus.Failed => $"❌ {item.Note}",
                _ => ""
            },
            NoteColor = item.Status switch
            {
                RenameStatus.Done => StatusColors.Done,
                RenameStatus.Skipped => "#606070",
                RenameStatus.Failed => StatusColors.Error,
                _ => "#9090A0"
            }
        }).ToList();

        _dialogService.ShowReport(
            "✅ 重命名完成报告",
            $"目标: {TargetPath}  |  完成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            new List<SummaryItem>
            {
                new() { Label = "共处理", Count = plan.Count, Color = "#E4E4F0" },
                new() { Label = "✅ 成功", Count = done, Color = StatusColors.Done },
                new() { Label = "跳过", Count = skipped, Color = "#9090A0" },
                new() { Label = "❌ 失败", Count = failed, Color = failed > 0 ? StatusColors.Error : "#606070" },
            },
            resultRows,
            System.Windows.Application.Current.MainWindow);
    }

    private bool ValidatePath()
    {
        if (string.IsNullOrWhiteSpace(TargetPath))
        {
            _dialogService.ShowWarning("请先选择目标文件或文件夹路径。", "提示");
            return false;
        }
        if (!File.Exists(TargetPath) && !Directory.Exists(TargetPath))
        {
            _dialogService.ShowError($"路径不存在：\n{TargetPath}", "错误");
            return false;
        }
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; // 先置标志，防止异常导致重复 Dispose
        _opCts?.Cancel();
        _opCts?.Dispose();
        _opCts = null;
        _audioService.Dispose();
    }
}
