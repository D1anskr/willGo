using System.Collections.ObjectModel;
using System.IO;
using FloatingDeskAssistant.Application;
using FloatingDeskAssistant.ViewModels.Base;

namespace FloatingDeskAssistant.ViewModels;

public sealed class BattleHistoryViewModel : ObservableObject, IDisposable
{
    private readonly Func<string> _getDirectory;
    private string _directory = "", _status = "";
    private bool _loading, _canListen = true, _disposed;
    private int _revision;
    private BattleHistoryEntry? _selectedEntry;
    public BattleHistoryViewModel(Func<string> getDirectory)
    {
        _getDirectory = getDirectory;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsLoading);
    }
    public ObservableCollection<BattleHistoryEntry> Entries { get; } = [];
    public BattleHistoryEntry? SelectedEntry { get => _selectedEntry; set => SetProperty(ref _selectedEntry, value); }
    public string DirectoryPath { get => _directory; private set => SetProperty(ref _directory, value); }
    public string Status { get => _status; set => SetProperty(ref _status, value); }
    public bool CanListen { get => _canListen; set => SetProperty(ref _canListen, value); }
    public bool IsEmpty => Entries.Count == 0;
    public bool IsLoading { get => _loading; private set { SetProperty(ref _loading, value); RefreshCommand.NotifyCanExecuteChanged(); } }
    public AsyncRelayCommand RefreshCommand { get; }

    public async Task RefreshAsync()
    {
        if (_disposed) return;
        var revision = ++_revision;
        IsLoading = true;
        Status = "正在读取历史记录…";
        try
        {
            var directory = Environment.ExpandEnvironmentVariables((_getDirectory() ?? "").Trim());
            if (!Path.IsPathFullyQualified(directory)) throw new ArgumentException("请先在语音设置中选择完整的录音保存目录。");
            DirectoryPath = Path.GetFullPath(directory);
            var entries = await Task.Run(() => BattleHistory.Read(directory));
            if (_disposed || revision != _revision) return;
            var selectedName = SelectedEntry?.Name;
            Entries.Clear();
            foreach (var entry in entries) Entries.Add(entry);
            SelectedEntry = entries.FirstOrDefault(entry => string.Equals(entry.Name, selectedName, StringComparison.OrdinalIgnoreCase));
            Status = !Directory.Exists(directory) ? "保存目录尚不存在，开始战斗后会按需创建。" :
                entries.Count == 0 ? "此目录暂无录音或文字记录。" : $"共 {entries.Count} 次记录 · 按开始时间从新到旧排列";
        }
        catch (Exception ex)
        {
            if (_disposed || revision != _revision) return;
            Entries.Clear(); SelectedEntry = null; Status = "无法读取历史记录：" + ex.Message;
        }
        finally
        {
            if (!_disposed && revision == _revision) { IsLoading = false; RaisePropertyChanged(nameof(IsEmpty)); }
        }
    }
    public void Dispose() { _disposed = true; ++_revision; }
}
