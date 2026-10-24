using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace UltimateKtv
{
    /// <summary>
    /// 歌手圖片格子的資料項。
    /// 實作 INotifyPropertyChanged，讓翻頁時可以重複使用同一組格子 (不重建 Button/Template)，
    /// 並在背景解碼完成後再更新 Photo。
    /// </summary>
    public class SingerDisplayItem : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private string? _photoPath;
        private bool _hasPhoto;
        private ImageSource? _photo;

        public string Name
        {
            get => _name;
            set => SetField(ref _name, value);
        }

        /// <summary>照片的本機檔案路徑 (沒有照片時為 null)。</summary>
        public string? PhotoPath
        {
            get => _photoPath;
            set => SetField(ref _photoPath, value);
        }

        public bool HasPhoto
        {
            get => _hasPhoto;
            set => SetField(ref _hasPhoto, value);
        }

        /// <summary>已解碼 (縮圖) 的照片，供 XAML 的 Image.Source 綁定。</summary>
        public ImageSource? Photo
        {
            get => _photo;
            set => SetField(ref _photo, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
