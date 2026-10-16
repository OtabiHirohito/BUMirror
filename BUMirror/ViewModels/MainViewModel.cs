using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using BUMirror.Models;

namespace BUMirror.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        #region INotifyPropertyChanged
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnProp([CallerMemberName] string? n = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        private bool Set<T>(ref T f, T v, [CallerMemberName] string? n = null)
        {
            if (EqualityComparer<T>.Default.Equals(f, v)) return false;
            f = v; OnProp(n); return true;
        }
        #endregion

        #region バインディングプロパティ

        private string _folderA = string.Empty;
        public string FolderA { get => _folderA; set { if (Set(ref _folderA, value)) OnProp(nameof(CanCompare)); } }

        private string _folderB = string.Empty;
        public string FolderB { get => _folderB; set { if (Set(ref _folderB, value)) OnProp(nameof(CanCompare)); } }

        private bool _recursive = false;
        public bool Recursive { get => _recursive; set => Set(ref _recursive, value); }

        private bool _isBusy = false;
        public bool IsBusy { get => _isBusy; set { Set(ref _isBusy, value); OnProp(nameof(CanCompare)); OnProp(nameof(IsEmpty)); } }

        private int _progress = 0;
        public int Progress { get => _progress; set => Set(ref _progress, value); }

        private string _statusText = "フォルダ A・B を選択して「比較実行」を押してください。";
        public string StatusText { get => _statusText; set => Set(ref _statusText, value); }

        private string _summaryA = string.Empty;
        public string SummaryA { get => _summaryA; set => Set(ref _summaryA, value); }

        private string _summaryB = string.Empty;
        public string SummaryB { get => _summaryB; set => Set(ref _summaryB, value); }

        private string _filterText = string.Empty;
        public string FilterText
        {
            get => _filterText;
            set { Set(ref _filterText, value); ApplyFilter(); }
        }

        private string _filterSide = "すべて";
        public string FilterSide
        {
            get => _filterSide;
            set { Set(ref _filterSide, value); ApplyFilter(); }
        }

        public bool CanCompare => !string.IsNullOrEmpty(FolderA) && !string.IsNullOrEmpty(FolderB) && !IsBusy;

        private ObservableCollection<FileEntry> _allEntries = new();

        private ObservableCollection<FileEntry> _entries = new();
        public ObservableCollection<FileEntry> Entries
        {
            get => _entries;
            private set
            {
                if (ReferenceEquals(_entries, value)) return;
                _entries = value;
                _entries.CollectionChanged += (s, e) => OnProp(nameof(IsEmpty));
                OnProp(nameof(Entries));
                OnProp(nameof(IsEmpty));
            }
        }

        public bool IsEmpty => Entries.Count == 0 && !IsBusy;

        public int SelectedCount => _selectedEntries.Count;
        private readonly List<FileEntry> _selectedEntries = new();

        /// <summary>現在インライン編集中のエントリ (未編集時は null)</summary>
        private FileEntry? _editingEntry;

        #endregion

        #region コマンド
        public ICommand BrowseFolderACommand { get; }
        public ICommand BrowseFolderBCommand { get; }
        public ICommand CompareCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand CopySelectedCommand { get; }
        public ICommand SelectACommand { get; }
        public ICommand SelectBCommand { get; }
        public ICommand DeselectAllCommand { get; }
        #endregion

        private CancellationTokenSource? _cts;

        public MainViewModel()
        {
            BrowseFolderACommand  = new RelayCommand(_ => BrowseFolder(isA: true));
            BrowseFolderBCommand  = new RelayCommand(_ => BrowseFolder(isA: false));
            CompareCommand        = new RelayCommand(async _ => await CompareAsync(), _ => CanCompare);
            CancelCommand         = new RelayCommand(_ => _cts?.Cancel(), _ => IsBusy);
            CopySelectedCommand   = new RelayCommand(async _ => await CopySelectedAsync(), _ => _selectedEntries.Count > 0 && !IsBusy);
            SelectACommand        = new RelayCommand(_ => RequestSelectA?.Invoke());
            SelectBCommand        = new RelayCommand(_ => RequestSelectB?.Invoke());
            DeselectAllCommand    = new RelayCommand(_ => RequestDeselectAll?.Invoke());

            LoadSettings();
        }

        private void LoadSettings()
        {
            var settings = AppSettings.Load();
            FolderA = settings.FolderA;
            FolderB = settings.FolderB;
            Recursive = settings.Recursive;
            FilterSide = settings.FilterSide;
        }

        public void SaveSettings()
        {
            new AppSettings
            {
                FolderA = this.FolderA,
                FolderB = this.FolderB,
                Recursive = this.Recursive,
                FilterSide = this.FilterSide
            }.Save();
        }

        // View から呼んでもらう選択コールバック
        public Action? RequestSelectA;
        public Action? RequestSelectB;
        public Action? RequestDeselectAll;

        #region フォルダ選択

        private void BrowseFolder(bool isA)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = isA ? "フォルダ A を選択" : "フォルダ B を選択",
                SelectedPath = isA ? FolderA : FolderB
            };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                if (isA) FolderA = dlg.SelectedPath;
                else     FolderB = dlg.SelectedPath;
            }
        }

        #endregion

        #region 比較処理

        private async Task CompareAsync()
        {
            if (!Directory.Exists(FolderA)) { System.Windows.MessageBox.Show("フォルダ A が見つかりません。", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (!Directory.Exists(FolderB)) { System.Windows.MessageBox.Show("フォルダ B が見つかりません。", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            IsBusy = true;
            Progress = 0;
            _lastReportedPct = -1;
            _allEntries.Clear();
            Entries.Clear();
            _selectedEntries.Clear();
            OnProp(nameof(SelectedCount));
            SummaryA = SummaryB = string.Empty;
            StatusText = "スキャン中...";

            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            try
            {
                var result = await Task.Run(() => DoCompare(FolderA, FolderB, Recursive, token), token);

                _allEntries = new ObservableCollection<FileEntry>(result);
                ApplyFilter();

                int onlyA = result.Count(e => e.ExistsIn == "A");
                int onlyB = result.Count(e => e.ExistsIn == "B");

                SummaryA = $"フォルダ A のみ: {onlyA} 件";
                SummaryB = $"フォルダ B のみ: {onlyB} 件";

                if (token.IsCancellationRequested)
                    StatusText = "中断されました。";
                else if (result.Count == 0)
                    StatusText = "✅ 差分なし。両フォルダのファイルは一致しています。";
                else
                    StatusText = $"比較完了: 差分 {result.Count} 件 (A のみ {onlyA} / B のみ {onlyB})";
            }
            catch (OperationCanceledException)
            {
                StatusText = "中断されました。";
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"エラー: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText = "エラーが発生しました。";
            }
            finally
            {
                IsBusy = false;
                Progress = 0;
                _cts?.Dispose();
                _cts = null;
            }
        }

        private List<FileEntry> DoCompare(string folderA, string folderB, bool recursive, CancellationToken token)
        {
            var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            var filesA = Directory.GetFiles(folderA, "*", option);
            var filesB = Directory.GetFiles(folderB, "*", option);

            int total = filesA.Length + filesB.Length;
            int done = 0;

            // フォルダA基準の相対パス→FileInfo辞書
            var dictA = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in filesA)
            {
                token.ThrowIfCancellationRequested();
                var rel = Path.GetRelativePath(folderA, f);
                dictA[rel] = new FileInfo(f);
                done++;
                ReportProgress(done, total);
            }

            var dictB = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in filesB)
            {
                token.ThrowIfCancellationRequested();
                var rel = Path.GetRelativePath(folderB, f);
                dictB[rel] = new FileInfo(f);
                done++;
                ReportProgress(done, total);
            }

            var result = new List<FileEntry>();

            // A にしかないファイル
            foreach (var kv in dictA)
            {
                if (!dictB.ContainsKey(kv.Key))
                {
                    result.Add(MakeEntry(kv.Value, kv.Key, "A", folderB));
                }
            }

            // B にしかないファイル
            foreach (var kv in dictB)
            {
                if (!dictA.ContainsKey(kv.Key))
                {
                    result.Add(MakeEntry(kv.Value, kv.Key, "B", folderA));
                }
            }

            var naturalComparer = new NaturalStringComparer();
            return result.OrderBy(e => e.ExistsIn)
                         .ThenBy(e => e.FileName, naturalComparer)
                         .ToList();
        }

        private static FileEntry MakeEntry(FileInfo fi, string relativePath, string side, string copyDestRoot)
        {
            return new FileEntry
            {
                FileName      = relativePath,
                FullPath      = fi.FullName,
                ExistsIn      = side,
                CopyDestDir   = Path.Combine(copyDestRoot, Path.GetDirectoryName(relativePath) ?? string.Empty),
                SizeStr       = FormatSize(fi.Length),
                LastWriteStr  = fi.LastWriteTime.ToString("yyyy/MM/dd HH:mm:ss"),
            };
        }

        private int _lastReportedPct = -1;

        private void ReportProgress(int done, int total)
        {
            if (total <= 0) return;
            int pct = (int)((double)done / total * 100);

            // ファイル 1 件ごとに Dispatcher へ投げると UI が飽和するため、
            // パーセントが変化したときだけ通知する (最大でも約 100 回)。
            if (pct == _lastReportedPct) return;
            _lastReportedPct = pct;
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() => Progress = pct);
        }

        private static string FormatSize(long bytes) =>
            bytes >= 1_073_741_824 ? $"{bytes / 1_073_741_824.0:F1} GB" :
            bytes >= 1_048_576     ? $"{bytes / 1_048_576.0:F1} MB"     :
            bytes >= 1_024         ? $"{bytes / 1_024.0:F1} KB"         :
                                     $"{bytes} B";

        #endregion

        #region フィルタ

        private void ApplyFilter()
        {
            var filtered = _allEntries.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(_filterText))
            {
                var keywords = _filterText.Split(new[] { ',', '，', '、' }, StringSplitOptions.RemoveEmptyEntries)
                                          .Select(k => k.Trim())
                                          .Where(k => !string.IsNullOrEmpty(k))
                                          .ToList();

                if (keywords.Any())
                {
                    filtered = filtered.Where(e => keywords.Any(k => e.FileName.Contains(k, StringComparison.OrdinalIgnoreCase)));
                }
            }

            if (_filterSide == "A のみ")
                filtered = filtered.Where(e => e.ExistsIn == "A");
            else if (_filterSide == "B のみ")
                filtered = filtered.Where(e => e.ExistsIn == "B");

            // 1 件ずつ Add すると CollectionChanged が N 回発生して重いため、
            // 絞り込み結果でコレクションを丸ごと差し替える（イベントは 1 回だけ）。
            Entries = new ObservableCollection<FileEntry>(filtered);
        }

        #endregion

        #region 選択管理

        public void UpdateSelection(object? selectedItems)
        {
            _selectedEntries.Clear();
            if (selectedItems is System.Collections.IList list)
                foreach (FileEntry e in list) _selectedEntries.Add(e);

            // 編集中のアイテムが選択から外れたら、名前変更モードを終了する
            if (_editingEntry != null && !_selectedEntries.Contains(_editingEntry))
                CommitRename(_editingEntry);

            OnProp(nameof(SelectedCount));
            CommandManager.InvalidateRequerySuggested();
        }

        #endregion

        #region 右クリックメニュー・操作 (場所を開く・削除・名前の変更)

        public void OpenLocation(FileEntry entry)
        {
            if (entry == null || !File.Exists(entry.FullPath))
            {
                System.Windows.MessageBox.Show("ファイルが見つかりません。", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{entry.FullPath}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"フォルダを開けませんでした: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>複数のエントリをまとめて削除する (確認は 1 回)。</summary>
        public void DeleteEntries(IList<FileEntry> targets)
        {
            if (targets == null || targets.Count == 0) return;

            var list = targets.Distinct().ToList();

            string message;
            if (list.Count == 1)
            {
                message = $"「{Path.GetFileName(list[0].FullPath)}」を削除してもよろしいですか？";
            }
            else
            {
                string preview = string.Join("\n", list.Take(5).Select(e => $"  {Path.GetFileName(e.FullPath)}"));
                if (list.Count > 5) preview += $"\n  … 他 {list.Count - 5} 件";
                message = $"選択された {list.Count} 件のファイルを削除してもよろしいですか？\n\n{preview}";
            }

            var confirm = System.Windows.MessageBox.Show(
                message,
                "削除の確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            int ok = 0;
            var errors = new List<string>();

            foreach (var entry in list)
            {
                try
                {
                    if (File.Exists(entry.FullPath))
                    {
                        File.Delete(entry.FullPath);
                    }
                    _allEntries.Remove(entry);
                    Entries.Remove(entry);
                    ok++;
                }
                catch (Exception ex)
                {
                    errors.Add($"{Path.GetFileName(entry.FullPath)}: {ex.Message}");
                }
            }

            int onlyA = _allEntries.Count(e => e.ExistsIn == "A");
            int onlyB = _allEntries.Count(e => e.ExistsIn == "B");
            SummaryA = $"フォルダ A のみ: {onlyA} 件";
            SummaryB = $"フォルダ B のみ: {onlyB} 件";
            StatusText = errors.Count == 0
                ? $"✅ ファイルを削除しました: {ok} 件"
                : $"削除完了: {ok} 件成功 / {errors.Count} 件失敗";

            if (errors.Count > 0)
            {
                System.Windows.MessageBox.Show(
                    $"一部のファイルの削除に失敗しました:\n\n{string.Join("\n", errors.Take(10))}",
                    "削除エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>単一エントリを削除する。</summary>
        public void DeleteEntry(FileEntry entry)
        {
            if (entry == null) return;
            DeleteEntries(new[] { entry });
        }

        /// <summary>選択中のエントリをまとめて削除する。</summary>
        public void DeleteSelected()
        {
            DeleteEntries(_selectedEntries.ToList());
        }

        public void StartRename(FileEntry entry)
        {
            if (entry == null) return;
            _editingEntry = entry;
            entry.EditingName = Path.GetFileName(entry.FullPath);
            entry.IsEditing = true;
        }

        public void CancelRename(FileEntry entry)
        {
            if (entry == null) return;
            entry.IsEditing = false;
            if (ReferenceEquals(_editingEntry, entry)) _editingEntry = null;
        }

        public void CommitRename(FileEntry entry)
        {
            if (entry == null || !entry.IsEditing) return;
            if (ReferenceEquals(_editingEntry, entry)) _editingEntry = null;

            string newName = entry.EditingName?.Trim() ?? string.Empty;
            string oldFullPath = entry.FullPath;
            string oldFileName = Path.GetFileName(oldFullPath);

            if (string.IsNullOrEmpty(newName) || newName == oldFileName)
            {
                entry.IsEditing = false;
                return;
            }

            if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                System.Windows.MessageBox.Show("ファイル名に無効な文字が含まれています。", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                entry.IsEditing = false;
                return;
            }

            string dir = Path.GetDirectoryName(oldFullPath) ?? string.Empty;
            string newFullPath = Path.Combine(dir, newName);

            if (File.Exists(newFullPath))
            {
                System.Windows.MessageBox.Show("同名のファイルが既に存在します。", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                entry.IsEditing = false;
                return;
            }

            try
            {
                if (File.Exists(oldFullPath))
                {
                    File.Move(oldFullPath, newFullPath);
                }

                entry.FullPath = newFullPath;

                // 相対パスの更新
                string rootFolder = entry.ExistsIn == "A" ? FolderA : FolderB;
                if (!string.IsNullOrEmpty(rootFolder) && Directory.Exists(rootFolder))
                {
                    entry.FileName = Path.GetRelativePath(rootFolder, newFullPath);
                }
                else
                {
                    string oldDirRel = Path.GetDirectoryName(entry.FileName) ?? string.Empty;
                    entry.FileName = string.IsNullOrEmpty(oldDirRel) ? newName : Path.Combine(oldDirRel, newName);
                }

                // コピー先ディレクトリの更新
                string otherFolder = entry.ExistsIn == "A" ? FolderB : FolderA;
                entry.CopyDestDir = Path.Combine(otherFolder, Path.GetDirectoryName(entry.FileName) ?? string.Empty);

                StatusText = $"名前を変更しました: {oldFileName} → {newName}";
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"名前の変更に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                entry.IsEditing = false;
            }
        }

        #endregion

        #region コピー処理

        private async Task CopySelectedAsync()
        {
            if (_selectedEntries.Count == 0) return;

            var targets = _selectedEntries.ToList();
            var confirm = System.Windows.MessageBox.Show(
                $"選択された {targets.Count} 件のファイルをコピーしますか?\n\n" +
                string.Join("\n", targets.Take(5).Select(e => $"  {e.FileName}  → フォルダ{(e.ExistsIn == "A" ? "B" : "A")}")) +
                (targets.Count > 5 ? $"\n  … 他 {targets.Count - 5} 件" : ""),
                "コピーの確認", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            IsBusy = true;
            Progress = 0;
            _lastReportedPct = -1;
            StatusText = "コピー中...";

            int ok = 0, fail = 0;
            var errors = new List<string>();

            await Task.Run(() =>
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    var entry = targets[i];
                    try
                    {
                        Directory.CreateDirectory(entry.CopyDestDir);
                        string dest = Path.Combine(entry.CopyDestDir, Path.GetFileName(entry.FullPath));
                        File.Copy(entry.FullPath, dest, overwrite: true);
                        ok++;
                    }
                    catch (Exception ex)
                    {
                        fail++;
                        errors.Add($"{entry.FileName}: {ex.Message}");
                    }
                    ReportProgress(i + 1, targets.Count);
                }
            });

            IsBusy = false;
            Progress = 0;

            if (fail == 0)
            {
                StatusText = $"✅ コピー完了: {ok} 件";
                // コピー済みを一覧から除去
                foreach (var e in targets) { _allEntries.Remove(e); Entries.Remove(e); }
                int onlyA = _allEntries.Count(e => e.ExistsIn == "A");
                int onlyB = _allEntries.Count(e => e.ExistsIn == "B");
                SummaryA = $"フォルダ A のみ: {onlyA} 件";
                SummaryB = $"フォルダ B のみ: {onlyB} 件";
            }
            else
            {
                StatusText = $"コピー完了: {ok} 件成功 / {fail} 件失敗";
                System.Windows.MessageBox.Show($"一部のファイルのコピーに失敗しました:\n\n{string.Join("\n", errors.Take(10))}",
                    "コピーエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        #endregion
    }

    // ─── シンプルな RelayCommand ───────────────────────────────
    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;
        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        { _execute = execute; _canExecute = canExecute; }
        public event EventHandler? CanExecuteChanged { add => CommandManager.RequerySuggested += value; remove => CommandManager.RequerySuggested -= value; }
        public bool CanExecute(object? p) => _canExecute?.Invoke(p) ?? true;
        public void Execute(object? p) => _execute(p);
    }

    /// <summary>
    /// 数値を考慮した自然な順序で比較する比較器 (Windows API 使用)
    /// </summary>
    public class NaturalStringComparer : IComparer<string>
    {
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int StrCmpLogicalW(string? x, string? y);

        public int Compare(string? x, string? y) => StrCmpLogicalW(x, y);
    }
}
