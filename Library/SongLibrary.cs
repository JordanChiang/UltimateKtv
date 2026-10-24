using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace UltimateKtv;

/// <summary>
/// 歌庫的寫入：歌曲的播放次數/音量/伴唱位置、我的最愛、點唱紀錄、YouTube 點播紀錄。
/// SQL 與 Access/SQLite 的差異都在這裡，呼叫端不寫 SQL。
/// 只動資料庫；呼叫端寫入後若需要，自行重新載入 SongDatas 的記憶體快取。
/// </summary>
public static class SongLibrary
{
	/// <summary>今日點唱清單的 User_Id (第一首歌開播時清空)。</summary>
	public const string TodayListUserId = "****";

	// ---------------- 歌曲 ----------------

	public static void SetPlayCount(string songId, int playCount) =>
		Db.ExecuteNonQuery("UPDATE ktv_Song SET Song_PlayCount = ? WHERE Song_Id = ?", playCount, songId);

	public static void SetVolume(string songId, int volume) =>
		Db.ExecuteNonQuery("UPDATE ktv_Song SET Song_Volume = ? WHERE Song_Id = ?", volume, songId);

	/// <summary>伴唱所在的音軌/聲道 (Song_Track)。</summary>
	public static void SetMusicTrack(string songId, int track) =>
		Db.ExecuteNonQuery("UPDATE ktv_Song SET Song_Track = ? WHERE Song_Id = ?", track, songId);

	// ---------------- YouTube 點播紀錄 ----------------

	/// <summary>播放次數加一；idOrUrl 可以是紀錄 Id (桌面點播) 或影片網址/ID (網路點播)。</summary>
	/// <returns>更新的筆數。</returns>
	public static int IncrementYoutubePlayCount(string idOrUrl)
	{
		string plusOne = Db.IsAccess ? "IIF(PlayCount IS NULL, 0, PlayCount) + 1" : "COALESCE(PlayCount, 0) + 1";
		return Db.ExecuteNonQuery($"UPDATE YoutubeSongList SET PlayCount = {plusOne} WHERE Id = ? OR Url = ?", idOrUrl, idOrUrl);
	}

	public static void ClearYoutubeHistory() => Db.ExecuteNonQuery("DELETE FROM YoutubeSongList");

	/// <summary>YoutubeSongList.Name 實際的欄位長度 (舊歌庫若加寬失敗會比 255 短)；開機檢查欄位後設定。</summary>
	public static int YoutubeNameMaxLength { get; set; } = 255;

	/// <summary>
	/// 記錄一部 YouTube 影片 (下載完成或匯入)。已有同一影片 ID，或同檔名 (匯入的檔案沒有影片 ID) 的紀錄時
	/// 不新增，回傳既有的 Id；同檔名的紀錄若還沒有影片 ID 就補上。
	/// </summary>
	/// <returns>紀錄 Id (例如「Y0017」)；失敗時回傳空字串。</returns>
	public static string RecordYoutubeSong(string videoId, string name, string filePath, string lang, int volume)
	{
		try
		{
			if (!string.IsNullOrEmpty(videoId))
			{
				var byUrl = Db.GetDataTable("SELECT Id FROM YoutubeSongList WHERE Url = ?", videoId);
				if (byUrl.Rows.Count > 0) return byUrl.Rows[0][0].ToString() ?? "";
			}

			string fileName = Path.GetFileName(filePath) ?? "";
			if (!string.IsNullOrEmpty(fileName))
			{
				var byFile = Db.GetDataTable("SELECT Id FROM YoutubeSongList WHERE FileName = ?", fileName);
				if (byFile.Rows.Count > 0)
				{
					string existingId = byFile.Rows[0][0].ToString() ?? "";
					if (!string.IsNullOrEmpty(videoId))
					{
						Db.ExecuteNonQuery("UPDATE YoutubeSongList SET Url = ? WHERE Id = ? AND (Url IS NULL OR Url = '')", videoId, existingId);
					}
					return existingId;
				}
			}

			// 新 Id：Y0001 起依序編號
			var maxIdTable = Db.GetDataTable("SELECT MAX(Id) FROM YoutubeSongList");
			string newId = "Y0001";
			if (maxIdTable.Rows.Count > 0 && maxIdTable.Rows[0][0] != DBNull.Value)
			{
				string maxId = maxIdTable.Rows[0][0].ToString() ?? "";
				if (maxId.StartsWith("Y") && int.TryParse(maxId.Substring(1), out int num)) newId = $"Y{(num + 1):D4}";
			}

			if (name.Length > YoutubeNameMaxLength) name = name.Substring(0, YoutubeNameMaxLength);
			Db.ExecuteNonQuery(@"INSERT INTO YoutubeSongList
				(Id, Name, CreatDate, Url, Singer, Lang, PlayCount, Length, FileName, FileSize, Track, Volume, WordCount)
				VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
				newId,
				name,
				DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss"),
				videoId,
				"",         // Singer
				lang ?? "",
				0,          // PlayCount：實際唱過才計
				0,          // Length：下載完成後另外寫入
				fileName,
				File.Exists(filePath) ? (int)Math.Min(new FileInfo(filePath).Length, int.MaxValue) : 0,
				3,          // Track
				volume,
				0);         // WordCount
			AppLogger.Log($"DB Write: Recorded YouTube song: {name} (Id: {newId})");
			return newId;
		}
		catch (Exception ex)
		{
			AppLogger.Log($"Error recording YouTube song: {ex.Message}");
			return "";
		}
	}

	/// <summary>寫入影片長度 (只在還沒有長度時)；idOrUrl 可以是紀錄 Id 或影片 ID。</summary>
	/// <returns>是否有更新。</returns>
	public static bool UpdateYoutubeSongLength(string idOrUrl, int lengthSeconds)
	{
		if (string.IsNullOrEmpty(idOrUrl) || lengthSeconds <= 0) return false;
		try
		{
			int rows = Db.ExecuteNonQuery("UPDATE YoutubeSongList SET Length = ? WHERE (Id = ? OR Url = ?) AND (Length IS NULL OR Length = 0)",
				lengthSeconds, idOrUrl, idOrUrl);
			if (rows > 0) AppLogger.Log($"DB Write: Updated Youtube song {idOrUrl} length to {lengthSeconds} seconds.");
			return rows > 0;
		}
		catch (Exception ex)
		{
			AppLogger.LogError($"Failed to update Youtube song length for {idOrUrl}", ex);
			return false;
		}
	}

	public static void DeleteYoutubeHistory(string id)
	{
		try
		{
			Db.ExecuteNonQuery("DELETE FROM YoutubeSongList WHERE Id = ?", id);
			AppLogger.Log($"Deleted Youtube History record: {id}");
		}
		catch (Exception ex)
		{
			AppLogger.Log($"Error deleting Youtube history: {ex.Message}");
		}
	}

	private static readonly Regex DurationLikeLang = new(@"^\d{1,2}(:\d{2}){1,2}$");

	/// <summary>
	/// 修正舊版留下的 YouTube 紀錄 (開機時做一次)：匯入的檔案沒有影片 ID 時從檔名補回、
	/// 歌名曾被截成 40 字或帶著「_{videoId}」後綴時從檔名還原、Lang 曾被寫成影片長度時清空。
	/// </summary>
	/// <returns>修正的筆數。</returns>
	public static int RepairYoutubeHistory()
	{
		int repaired = 0;
		DataTable rows;
		try
		{
			rows = Db.GetDataTable("SELECT Id, Name, Url, Lang, FileName FROM YoutubeSongList");
		}
		catch (Exception ex)
		{
			AppLogger.Log($"RepairYoutubeHistory: cannot read YoutubeSongList: {ex.Message}");
			return 0;
		}

		foreach (DataRow row in rows.Rows)
		{
			string id = Convert.ToString(row["Id"]) ?? "";
			string name = row["Name"] == DBNull.Value ? "" : Convert.ToString(row["Name"]) ?? "";
			string url = row["Url"] == DBNull.Value ? "" : Convert.ToString(row["Url"]) ?? "";
			string lang = row["Lang"] == DBNull.Value ? "" : Convert.ToString(row["Lang"]) ?? "";
			string fileName = row["FileName"] == DBNull.Value ? "" : Convert.ToString(row["FileName"]) ?? "";
			if (string.IsNullOrEmpty(id)) continue;

			try
			{
				string recoveredId = string.IsNullOrEmpty(url) ? YoutubeVideoId.FromFileName(fileName) : "";
				if (recoveredId.Length > 0)
				{
					Db.ExecuteNonQuery("UPDATE YoutubeSongList SET Url = ? WHERE Id = ? AND (Url IS NULL OR Url = '')", recoveredId, id);
					AppLogger.Log($"DB Write: Recovered YouTube video ID '{recoveredId}' for {id} from file name.");
					repaired++;
				}

				string fixedName = RestoredYoutubeName(name, fileName);
				if (fixedName != name)
				{
					Db.ExecuteNonQuery("UPDATE YoutubeSongList SET Name = ? WHERE Id = ?", fixedName, id);
					AppLogger.Log($"DB Write: Restored YouTube name for {id}: '{fixedName}'.");
					repaired++;
				}

				if (DurationLikeLang.IsMatch(lang))
				{
					Db.ExecuteNonQuery("UPDATE YoutubeSongList SET Lang = '' WHERE Id = ?", id);
					AppLogger.Log($"DB Write: Cleared duration-like Lang '{lang}' for {id}.");
					repaired++;
				}
			}
			catch (Exception ex)
			{
				AppLogger.Log($"RepairYoutubeHistory: failed to repair {id}: {ex.Message}");
			}
		}
		return repaired;
	}

	/// <summary>匯入的歌名是整個檔名 (含「_{videoId}」)，或舊版截在 40 字：從檔名還原；其他情況不變。</summary>
	private static string RestoredYoutubeName(string name, string fileName)
	{
		string fileTitle = YoutubeVideoId.TitleFromFileName(fileName);
		if (fileTitle.Length == 0 || name == fileTitle) return name;

		string safeName = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
		string fileNameOnly = Path.GetFileNameWithoutExtension(fileName);
		string fixedName = name;
		if (name == fileNameOnly)
			fixedName = fileTitle;
		else if (name.Length == 40 && (fileTitle.StartsWith(safeName, StringComparison.Ordinal) || fileNameOnly.StartsWith(safeName, StringComparison.Ordinal)))
			fixedName = fileTitle;
		// 不可超過實際欄位長度 (舊的 40 字欄位會讓 UPDATE 失敗)
		return fixedName.Length > YoutubeNameMaxLength ? fixedName.Substring(0, YoutubeNameMaxLength) : fixedName;
	}

	// ---------------- 我的最愛 ----------------

	public static bool IsFavorite(string userId, string songId) =>
		CountOf("SELECT COUNT(*) FROM ktv_Favorite WHERE User_Id = ? AND Song_Id = ?", userId, songId) > 0;

	/// <returns>是否新增成功 (已在最愛中也回傳 false)。</returns>
	public static bool AddFavorite(string userId, string songId)
	{
		if (IsFavorite(userId, songId)) return false;
		return Db.ExecuteNonQuery("INSERT INTO ktv_Favorite (User_Id, Song_Id) VALUES (?, ?)", userId, songId) > 0;
	}

	// ---------------- 點唱紀錄 ----------------

	/// <summary>清空今日點唱清單。</summary>
	public static void ClearTodayList()
	{
		Db.ExecuteNonQuery("DELETE FROM ktv_Favorite WHERE User_Id = ?", TodayListUserId);
		AppLogger.Log($"DB Write: Cleared today's play list (User_Id = {TodayListUserId})");
	}

	/// <summary>
	/// 記錄一首歌被唱過：加入今日點唱清單，並加入這次開機的點唱紀錄 (User_Id = "MM/dd HH:mm")。
	/// 點唱紀錄最多保留 maxSessions 次開機，超過時刪掉最舊的一次 (含 ktv_User 的使用者)。
	/// </summary>
	public static void RecordPlay(string songId, string sessionUserId, int maxSessions)
	{
		if (string.IsNullOrEmpty(songId)) return;

		// 1. 今日點唱清單
		if (AddFavorite(TodayListUserId, songId))
		{
			AppLogger.Log($"DB Write: Added Song_Id={songId} to today's play list");
		}

		// 2. 這次開機的點唱紀錄：達到上限 (且這次開機還沒有紀錄) 時刪掉最舊的一次
		var sessions = Db.GetDataTable("SELECT DISTINCT User_Id FROM ktv_Favorite").Rows.Cast<DataRow>()
			.Select(r => Convert.ToString(r[0]) ?? "")
			.Where(IsSessionUserId)
			.OrderBy(id => id, StringComparer.Ordinal)
			.ToList();

		if (!sessions.Contains(sessionUserId) && sessions.Count >= maxSessions && sessions.Count > 0)
		{
			string oldest = sessions[0];
			int deleted = Db.ExecuteNonQuery("DELETE FROM ktv_Favorite WHERE User_Id = ?", oldest);
			AppLogger.Log($"DB Write: Deleted oldest timestamp records: User_Id={oldest}, rows deleted={deleted}");
			try
			{
				Db.ExecuteNonQuery("DELETE FROM ktv_User WHERE User_Id = ?", oldest);
			}
			catch (Exception ex)
			{
				AppLogger.LogError("Could not delete from ktv_User", ex);
			}
		}

		// 點唱紀錄以使用者顯示，ktv_User 要有這個 User_Id
		try
		{
			if (CountOf("SELECT COUNT(*) FROM ktv_User WHERE User_Id = ?", sessionUserId) == 0)
			{
				Db.ExecuteNonQuery("INSERT INTO ktv_User (User_Id, User_Name) VALUES (?, ?)", sessionUserId, sessionUserId);
				AppLogger.Log($"DB Write: Created User_Id in ktv_User: {sessionUserId}");
			}
		}
		catch (Exception ex)
		{
			AppLogger.LogError("Could not check/create User_Id in ktv_User", ex);
		}

		if (AddFavorite(sessionUserId, songId))
		{
			AppLogger.Log($"DB Write: Recorded play history: User_Id={sessionUserId}, Song_Id={songId}");
		}
	}

	/// <summary>點唱紀錄的 User_Id 格式 "MM/dd HH:mm"。</summary>
	public static bool IsSessionUserId(string userId)
	{
		if (string.IsNullOrEmpty(userId)) return false;
		return userId.Length == 11 &&
			   char.IsDigit(userId[0]) && char.IsDigit(userId[1]) &&
			   userId[2] == '/' &&
			   char.IsDigit(userId[3]) && char.IsDigit(userId[4]) &&
			   userId[5] == ' ' &&
			   char.IsDigit(userId[6]) && char.IsDigit(userId[7]) &&
			   userId[8] == ':' &&
			   char.IsDigit(userId[9]) && char.IsDigit(userId[10]);
	}

	private static int CountOf(string sql, params object?[] args)
	{
		var table = Db.GetDataTable(sql, args);
		return table.Rows.Count > 0 ? Convert.ToInt32(table.Rows[0][0]) : 0;
	}
}
