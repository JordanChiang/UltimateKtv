using System;

namespace UltimateKtv.Playback
{
    /// <summary>
    /// 主控台音量滑桿背後的規則：每首歌開播用哪個音量、音量鎖定、靜音、以及播完要不要把調整後的音量存回歌庫。
    /// 音量 0~100，滑桿以 <see cref="Step"/> 為單位對齊。Level 是滑桿目前的值，畫面依它顯示；
    /// 滑桿值改變時 (對齊後) 呼叫 <see cref="Set"/>。只在 UI 執行緒使用。
    /// </summary>
    public sealed class SongVolume
    {
        public const int Step = 5;
        public const int Max = 100;
        /// <summary>歌庫沒有記錄音量的歌，開播用這個音量。</summary>
        public const int DefaultSongVolume = 60;
        /// <summary>靜音前的音量是 0 時，解除靜音改用這個音量。</summary>
        public const int UnmuteVolume = 50;

        private double _beforeMute;

        /// <summary>
        /// 滑桿目前的值；與 XAML 滑桿的初始值相同。
        /// 滑桿只把離 <see cref="Step"/> 倍數超過 0.01 的值拉回去，所以可能帶有很小的小數。
        /// </summary>
        public double Level { get; private set; } = Max;
        /// <summary>音量對齊後為 0 時為靜音。</summary>
        public bool IsMuted { get; private set; }
        /// <summary>音量鎖定：之後每首歌都用 <see cref="LockedLevel"/> 開播，也不存回歌庫。</summary>
        public bool IsLocked { get; private set; }
        /// <summary>鎖定當下的音量 (取整數)。</summary>
        public int LockedLevel { get; private set; } = 60;
        /// <summary>這首歌開播時的音量；播完時音量與它不同才存回歌庫。</summary>
        public int OpenedLevel { get; private set; } = 30;

        /// <summary>滑桿值對齊到最接近的 <see cref="Step"/> 倍數。</summary>
        public static double Snap(double value) => Math.Round(value / Step) * Step;

        /// <summary>滑桿值改變了 (已對齊)。</summary>
        public void Set(double level)
        {
            Level = level;
            IsMuted = Snap(level) == 0;
        }

        /// <summary>音量加一格 (最多 <see cref="Max"/>)，回傳新的音量。</summary>
        public double Up() => Change(Math.Min(Max, Level + Step));

        /// <summary>音量減一格 (最少 0)，回傳新的音量。</summary>
        public double Down() => Change(Math.Max(0, Level - Step));

        /// <summary>靜音或解除靜音，回傳新的音量；解除時回到靜音前的音量 (那時是 0 就用 <see cref="UnmuteVolume"/>)。</summary>
        public double ToggleMute()
        {
            IsMuted = !IsMuted;
            double target;
            if (IsMuted)
            {
                _beforeMute = Level;
                target = 0;
            }
            else
            {
                target = _beforeMute > 0 ? _beforeMute : UnmuteVolume;
            }
            // 音量沒變時滑桿不會觸發變更，靜音狀態維持切換後的值
            return Change(target);
        }

        /// <summary>鎖定目前的音量。</summary>
        public void Lock()
        {
            IsLocked = true;
            LockedLevel = (int)Level;
        }

        public void Unlock() => IsLocked = false;

        /// <summary>開始開播一首歌：先把音量設成待播清單記錄的音量 (鎖定時為鎖定音量)，回傳新的音量。</summary>
        public double SongStarting(int queuedVolume) => Change(IsLocked ? LockedLevel : queuedVolume);

        /// <summary>
        /// 歌曲開檔完成：決定這首歌的開播音量 (歌庫記錄的音量，沒有記錄時 <see cref="DefaultSongVolume"/>；鎖定時為鎖定音量)，
        /// 播完時以它判斷使用者有沒有調整過。回傳開播音量；未鎖定時呼叫端要把滑桿設成它。
        /// </summary>
        public int SongOpened(int? songVolume)
        {
            OpenedLevel = IsLocked ? LockedLevel : songVolume ?? DefaultSongVolume;
            return OpenedLevel;
        }

        /// <summary>
        /// 歌曲播完、切歌或重唱時要存回歌庫的音量 (取整數)；沒有調整過、鎖定中或滑桿在 0 時為 null。
        /// YouTube 與不在歌庫的歌由呼叫端排除。
        /// </summary>
        public int? VolumeToSave()
        {
            if (Level == 0 || Level > Max) return null;
            if (IsLocked) return null;
            int level = (int)Level;
            return level != OpenedLevel ? level : null;
        }

        /// <summary>與滑桿相同：值限制在 0~<see cref="Max"/>，有變才觸發變更。</summary>
        private double Change(double level)
        {
            level = Math.Max(0, Math.Min(Max, level));
            if (level != Level) Set(level);
            return level;
        }
    }
}
