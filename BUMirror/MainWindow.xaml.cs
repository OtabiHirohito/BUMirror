using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BUMirror.ViewModels;

namespace BUMirror
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _vm;
        private bool _isDraggingSelection = false;

        public MainWindow()
        {
            InitializeComponent();
            _vm = new MainViewModel();
            DataContext = _vm;

            // VM からの選択コールバック
            _vm.RequestSelectA     = () => SelectBySide("A");
            _vm.RequestSelectB     = () => SelectBySide("B");
            _vm.RequestDeselectAll = () => MainListView.UnselectAll();

            this.Closed += (s, e) => _vm.SaveSettings();
        }

        private void SelectBySide(string side)
        {
            MainListView.SelectedItems.Clear();
            foreach (BUMirror.Models.FileEntry item in MainListView.Items)
            {
                if (item.ExistsIn == side)
                {
                    MainListView.SelectedItems.Add(item);
                }
            }
        }

        // ── 選択変更 ───────────────────────────────────────────
        private void MainListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _vm.UpdateSelection(MainListView.SelectedItems);
        }

        // ── フィルタ ───────────────────────────────────────────
        private void FilterSideCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_vm == null) return;
            if (sender is System.Windows.Controls.ComboBox cb &&
                cb.SelectedItem is System.Windows.Controls.ComboBoxItem item)
            {
                _vm.FilterSide = item.Content?.ToString() ?? "すべて";
            }
        }

        // ── フォルダ D&D ───────────────────────────────────────
        // TextBox は DragEnter/DragOver/Drop を「処理済み」にしてしまい、
        // FileDrop を拒否するため、トンネリングする Preview イベントで受け取る。
        private void FolderTextBox_PreviewDragOver(object sender, System.Windows.DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)
                ? System.Windows.DragDropEffects.Copy
                : System.Windows.DragDropEffects.None;
            e.Handled = true;
        }

        private void FolderTextBox_PreviewDrop(object sender, System.Windows.DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return;
            var paths = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
            if (paths == null || paths.Length == 0) return;

            string dropped = paths[0];
            if (!Directory.Exists(dropped)) return;

            // Tag="A" or "B" で区別
            if (sender is System.Windows.Controls.TextBox tb)
            {
                if (tb.Tag?.ToString() == "A") _vm.FolderA = dropped;
                else                           _vm.FolderB = dropped;
            }
            e.Handled = true;
        }

        // ── ドラッグ複数選択 ───────────────────────────────────
        private System.Windows.Point _dragStartPoint;

        private void MainListView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // スクロールバーやヘッダーのクリック時はドラッグ選択をスキップ
            var src = e.OriginalSource as DependencyObject;
            if (src != null && (FindAncestor<System.Windows.Controls.Primitives.ScrollBar>(src) != null ||
                                FindAncestor<GridViewColumnHeader>(src) != null))
            {
                _isDraggingSelection = false;
                return;
            }

            _dragStartPoint = e.GetPosition(MainListView);
            _isDraggingSelection = true;
        }

        private void MainListView_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_isDraggingSelection || e.LeftButton != MouseButtonState.Pressed) return;

            var currentPoint = e.GetPosition(MainListView);

            // 一定距離以上ドラッグした場合に選択範囲を更新
            if ((currentPoint - _dragStartPoint).Length <= 3) return;

            double startY = Math.Min(_dragStartPoint.Y, currentPoint.Y);
            double endY = Math.Max(_dragStartPoint.Y, currentPoint.Y);

            // 仮想化により実体化されているのは表示中の行だけ。
            // 全 Items を走査せず、表示中の ListViewItem のみを対象にする。
            var panel = FindDescendant<VirtualizingStackPanel>(MainListView);
            if (panel == null) return;

            foreach (var child in panel.Children)
            {
                if (child is not System.Windows.Controls.ListViewItem lvi) continue;

                var pt = lvi.TranslatePoint(new System.Windows.Point(0, 0), MainListView);
                double itemTop = pt.Y;
                double itemBottom = itemTop + lvi.ActualHeight;

                if (itemBottom >= startY && itemTop <= endY && !lvi.IsSelected)
                {
                    lvi.IsSelected = true;
                }
            }
        }

        private void Window_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDraggingSelection = false;
        }

        private static T? FindAncestor<T>(DependencyObject? obj) where T : DependencyObject
        {
            while (obj != null)
            {
                if (obj is T result) return result;
                obj = VisualTreeHelper.GetParent(obj);
            }
            return null;
        }

        private static T? FindDescendant<T>(DependencyObject? obj) where T : DependencyObject
        {
            if (obj == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(obj);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(obj, i);
                if (child is T match) return match;
                var result = FindDescendant<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        // ── ダブルクリックでファイルを開く ───────────────────────
        private void OnListViewItemMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Controls.ListViewItem item && item.Content is BUMirror.Models.FileEntry entry)
            {
                if (File.Exists(entry.FullPath))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = entry.FullPath,
                            UseShellExecute = true
                        });
                    }
                    catch (System.Exception ex)
                    {
                        System.Windows.MessageBox.Show($"ファイルを開けませんでした: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    System.Windows.MessageBox.Show("ファイルが見つかりません。", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        // ── 右クリックで項目選択 & コンテキストメニュー ───────────────
        private void OnListViewItemPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Controls.ListViewItem lvi && !lvi.IsSelected)
            {
                // 未選択のアイテムを右クリックしたときは、そのアイテムだけを選択する
                MainListView.UnselectAll();
                lvi.IsSelected = true;
                lvi.Focus();
            }
        }

        private void OnContextMenuOpenLocationClick(object sender, RoutedEventArgs e)
        {
            var entry = (sender as MenuItem)?.DataContext as BUMirror.Models.FileEntry
                        ?? MainListView.SelectedItem as BUMirror.Models.FileEntry;
            if (entry != null)
            {
                _vm.OpenLocation(entry);
            }
        }

        private void OnContextMenuRenameClick(object sender, RoutedEventArgs e)
        {
            var entry = (sender as MenuItem)?.DataContext as BUMirror.Models.FileEntry
                        ?? MainListView.SelectedItem as BUMirror.Models.FileEntry;
            if (entry != null)
            {
                _vm.StartRename(entry);
            }
        }

        private void OnContextMenuDeleteClick(object sender, RoutedEventArgs e)
        {
            var entry = (sender as MenuItem)?.DataContext as BUMirror.Models.FileEntry
                        ?? MainListView.SelectedItem as BUMirror.Models.FileEntry;
            if (entry == null) return;

            // クリックしたアイテムが選択に含まれていれば選択中すべてを削除、
            // 含まれていなければそのアイテムだけを削除する。
            if (MainListView.SelectedItems.Contains(entry))
                _vm.DeleteSelected();
            else
                _vm.DeleteEntry(entry);
        }

        // ── キー操作 (F2 で名前変更 / Delete で削除) ────────────
        private void MainListView_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.F2)
            {
                if (MainListView.SelectedItems.Count == 1 && MainListView.SelectedItem is BUMirror.Models.FileEntry entry)
                {
                    _vm.StartRename(entry);
                    e.Handled = true;
                }
            }
            else if (e.Key == System.Windows.Input.Key.Delete)
            {
                // 名前変更中 (TextBox にフォーカス) はテキスト編集を優先する
                if (Keyboard.FocusedElement is not System.Windows.Controls.TextBox &&
                    MainListView.SelectedItems.Count > 0)
                {
                    _vm.DeleteSelected();
                    e.Handled = true;
                }
            }
        }

        // ── インライン名前変更 TextBox イベント ─────────────────────
        // 編集開始時に表示されるタイミングでフォーカスを与える
        // (仮想化のリサイクルで Loaded が期待通り発火しないため IsVisibleChanged を使う)
        private void RenameTextBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is not true) return;
            if (sender is System.Windows.Controls.TextBox tb)
            {
                tb.Focus();
                string text = tb.Text;
                int extIndex = text.LastIndexOf('.');
                if (extIndex > 0)
                {
                    tb.Select(0, extIndex);
                }
                else
                {
                    tb.SelectAll();
                }
            }
        }

        private void RenameTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBox tb && tb.DataContext is BUMirror.Models.FileEntry entry)
            {
                if (e.Key == System.Windows.Input.Key.Enter)
                {
                    tb.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
                    _vm.CommitRename(entry);
                    e.Handled = true;
                }
                else if (e.Key == System.Windows.Input.Key.Escape)
                {
                    _vm.CancelRename(entry);
                    e.Handled = true;
                }
            }
        }

        private void RenameTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.TextBox tb && tb.DataContext is BUMirror.Models.FileEntry entry)
            {
                if (entry.IsEditing)
                {
                    tb.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
                    _vm.CommitRename(entry);
                }
            }
        }
    }
}
