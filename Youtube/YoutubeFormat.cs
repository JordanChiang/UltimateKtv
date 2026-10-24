using System;
using System.Linq;
using YoutubeExplode.Videos.Streams;

namespace UltimateKtv
{
    /// <summary>
    /// YouTube 下載要抓哪種影像/音訊：yt-dlp 的客戶端與格式字串，以及 YoutubeExplode 退路的串流選擇。
    /// 兩條路徑規則相同：影像優先 H.264 (各機器都能硬體解碼)，不超過 maxHeight；聲音優先 AAC-LC m4a (140)，
    /// 合併成 mp4 不需轉碼。主程式與連網測試共用 (測試專案以連結原始檔編譯，不可引用 Loc 等主程式類別)。
    /// </summary>
    public static class YoutubeFormat
    {
        // yt-dlp 用預設客戶端 + android：只指定 android,web 時 YouTube 只給 format 18
        // (360p 影音合併，舊影片是 HE-AAC 48k)，高畫質設定無效，消人聲也會殘留人聲高頻；
        // 預設客戶端有 DASH 的 AAC-LC 128k (140) 與各解析度影像。android 留著當預設客戶端失效時的退路 (format 18)。
        public const string DownloadClients = "default,android";

        /// <summary>
        /// yt-dlp 的 -f 格式字串。只選 https：m3u8 (HLS) 分段格式的進度會亂跳。
        /// 用 bv (純影像) 而非 bv*：bv* 會選到位元率較高的 format 18，又變回低音質。
        /// 備選 (/) 只在選格式時生效：影片沒有 1080p 會選較低的，但選定後下載失敗不會降級。
        /// </summary>
        public static string YtDlpFormat(int maxHeight) =>
            $"bv[height<={maxHeight}][vcodec^=avc1][protocol=https]+ba[ext=m4a][protocol=https]" +
            $"/bv[height<={maxHeight}][protocol=https]+ba[protocol=https]" +
            $"/b[height<={maxHeight}]/b";

        /// <summary>
        /// YoutubeExplode 退路的純影像與純音訊串流 (YouTube 已不提供影音合併串流)；任一個沒有就回傳 null。
        /// </summary>
        public static (VideoOnlyStreamInfo? Video, AudioOnlyStreamInfo? Audio) SelectStreams(StreamManifest manifest, int maxHeight)
        {
            var video = manifest.GetVideoOnlyStreams()
                .Where(s => s.VideoQuality.MaxHeight <= maxHeight)
                .OrderByDescending(s => s.VideoCodec.StartsWith("avc1", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(s => s.VideoQuality)
                .ThenByDescending(s => s.Bitrate)
                .FirstOrDefault();
            var audio = manifest.GetAudioOnlyStreams()
                .OrderByDescending(s => s.Container == Container.Mp4)
                .ThenByDescending(s => s.Bitrate)
                .FirstOrDefault();
            return (video, audio);
        }
    }
}
