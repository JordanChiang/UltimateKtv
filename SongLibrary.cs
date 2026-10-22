using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

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
