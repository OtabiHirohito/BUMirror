using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BUMirror.Models
{
    /// <summary>
    /// 片側にしか存在しないファイルのエントリ
    /// </summary>
    public class FileEntry : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private string _fileName = string.Empty;
        /// <summary>ファイル名</summary>
        public string FileName
        {
            get => _fileName;
            set
            {
                if (_fileName != value)
                {
                    _fileName = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _fullPath = string.Empty;
        /// <summary>存在する側のフルパス</summary>
        public string FullPath
        {
            get => _fullPath;
            set
            {
                if (_fullPath != value)
                {
                    _fullPath = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _existsIn = string.Empty;
        /// <summary>ファイルが存在するフォルダ側 ("A" or "B")</summary>
        public string ExistsIn
        {
            get => _existsIn;
            set
            {
                if (_existsIn != value)
                {
                    _existsIn = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _copyDestDir = string.Empty;
        /// <summary>コピー先ディレクトリ（存在しない側）</summary>
        public string CopyDestDir
        {
            get => _copyDestDir;
            set
            {
                if (_copyDestDir != value)
                {
                    _copyDestDir = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _sizeStr = string.Empty;
        /// <summary>ファイルサイズ（フォーマット済み）</summary>
        public string SizeStr
        {
            get => _sizeStr;
            set
            {
                if (_sizeStr != value)
                {
                    _sizeStr = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _lastWriteStr = string.Empty;
        /// <summary>更新日時（フォーマット済み）</summary>
        public string LastWriteStr
        {
            get => _lastWriteStr;
            set
            {
                if (_lastWriteStr != value)
                {
                    _lastWriteStr = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _isEditing;
        /// <summary>インライン編集状態か</summary>
        public bool IsEditing
        {
            get => _isEditing;
            set
            {
                if (_isEditing != value)
                {
                    _isEditing = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _editingName = string.Empty;
        /// <summary>編集中のファイル名</summary>
        public string EditingName
        {
            get => _editingName;
            set
            {
                if (_editingName != value)
                {
                    _editingName = value;
                    OnPropertyChanged();
                }
            }
        }
    }
}
