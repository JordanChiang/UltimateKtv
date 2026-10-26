using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UltimateKtv
{
    /// <summary>手機播放控制指令要主程式做的事。正式版是 RemoteControl (切到 UI 執行緒)，測試用假的。</summary>
    public interface IRemoteControl
    {
        void Skip();
        void SwitchToVocal();
        void SwitchToMusic();
        void MoveToFirst(Guid id);
        void MoveToNext(Guid id);
        void Remove(Guid id);
        void VolumeUp();
        void VolumeDown();
        /// <summary>切換音量鎖定，回傳切換後是否鎖定。</summary>
        bool ToggleVolumeLock();
        void PitchUp();
        void PitchDown();
        void PitchReset();
        void Quit(bool shutdownWindows);
    }

    /// <summary>
    /// 手機的播放控制指令 (切歌、人聲/伴唱、待播清單移動、音量、升降 Key、結束)：queryType → 呼叫 <see cref="IRemoteControl"/>，
    /// 回傳給網頁的 JSON。成功為 {success:true, message}；value 不是 GUID 或主程式丟出例外時為錯誤 JSON (<see cref="RemoteJson.Error"/>)。
    /// 新增指令時在 <see cref="Commands"/> 加一列，HttpServer 會自動接上。
    /// </summary>
    public static class RemoteCommands
    {
        /// <summary>一個指令：執行後回傳成功訊息的語言 key；ErrorKey 是失敗時錯誤 JSON 的 Error 欄位。</summary>
        private sealed record Command(string ErrorKey, Func<IRemoteControl, string, string> Run);

        private static readonly Dictionary<string, Command> Commands = new()
        {
            ["skipsong"] = Do("Skip Error", r => r.Skip(), "Web.Msg.SkipSent"),
            ["vocal"] = Do("Vocal Switch Error", r => r.SwitchToVocal(), "Web.Msg.Vocal"),
            ["music"] = Do("Music Switch Error", r => r.SwitchToMusic(), "Web.Msg.Music"),
            ["movetofirst"] = WithId((r, id) => r.MoveToFirst(id), "Web.Msg.MovedToFirst"),
            ["removefromlist"] = WithId((r, id) => r.Remove(id), "Web.Msg.Removed"),
            ["movetonext"] = WithId((r, id) => r.MoveToNext(id), "Web.Msg.MovedNext"),
            ["volup"] = Do("VolumeUp Error", r => r.VolumeUp(), "Web.Msg.VolUp"),
            ["voldown"] = Do("VolumeDown Error", r => r.VolumeDown(), "Web.Msg.VolDown"),
            ["vollock"] = new("ToggleVolumeLock Error", (r, _) => r.ToggleVolumeLock() ? "Web.Msg.VolLocked" : "Web.Msg.VolUnlocked"),
            ["pitchup"] = Do("PitchUp Error", r => r.PitchUp(), "Web.Msg.PitchUp"),
            ["pitchdown"] = Do("PitchDown Error", r => r.PitchDown(), "Web.Msg.PitchDown"),
            ["pitchreset"] = Do("PitchReset Error", r => r.PitchReset(), "Web.Msg.PitchReset"),
            ["quit"] = Do("QuitApp Error", r => r.Quit(shutdownWindows: false), "Web.Msg.Quitting"),
            ["shutdownwin"] = Do("ShutdownWin Error", r => r.Quit(shutdownWindows: true), "Web.Msg.ShuttingDown"),
        };

        /// <summary>所有指令的 queryType。</summary>
        public static IEnumerable<string> Names => Commands.Keys;

        /// <summary>
        /// 執行一個指令；queryType 不是控制指令時回傳 false。text 把語言 key 換成給這個裝置的文字 (Loc.T)。
        /// </summary>
        public static bool TryRun(string queryType, string value, IRemoteControl remote, Func<string, string> text, out string json)
        {
            if (!Commands.TryGetValue(queryType, out var command))
            {
                json = "";
                return false;
            }

            try
            {
                string messageKey = command.Run(remote, value);
                json = JsonConvert.SerializeObject(new { success = true, message = text(messageKey) });
            }
            catch (InvalidIdException)
            {
                json = RemoteJson.Error("Invalid ID", "Provided ID is not a valid GUID");
            }
            catch (Exception ex)
            {
                json = RemoteJson.Error(command.ErrorKey, ex.Message);
            }
            return true;
        }

        private static Command Do(string errorKey, Action<IRemoteControl> action, string messageKey) =>
            new(errorKey, (r, _) => { action(r); return messageKey; });

        /// <summary>value 是待播清單項目的 Id (GUID)。</summary>
        private static Command WithId(Action<IRemoteControl, Guid> action, string messageKey) =>
            new("Error", (r, value) =>
            {
                if (!Guid.TryParse(value, out Guid id)) throw new InvalidIdException();
                action(r, id);
                return messageKey;
            });

        private sealed class InvalidIdException : Exception { }
    }

    /// <summary>手機 API 共用的 JSON 回應格式。</summary>
    public static class RemoteJson
    {
        /// <summary>錯誤回應：[{"Error": key, "Message": value}]；key 或 value 空白時為空陣列。</summary>
        public static string Error(string key, string value)
        {
            if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
            {
                JArray val = new JArray();
                JObject val2 = new JObject();
                val2.Add("Error", JToken.FromObject(key));
                val2.Add("Message", JToken.FromObject(value));
                val.Add(val2);
                return JsonConvert.SerializeObject(val);
            }
            return JsonConvert.SerializeObject(new JArray());
        }
    }
}
