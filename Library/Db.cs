using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Data.OleDb;
using System.Data.SQLite;
using System.IO;

namespace UltimateKtv;

public enum DbKind
{
	Access,
	SQLite
}

/// <summary>
/// Single entry point for the song database. Uses CrazySong.db (SQLite) when it exists next to the exe,
/// otherwise CrazySong.mdb (Access). Callers write "?" placeholders and pass values in the same order;
/// both providers bind them positionally.
/// </summary>
public static class Db
{
	public const string AccessFileName = "CrazySong.mdb";
	public const string SQLiteFileName = "CrazySong.db";

	public static DbKind Kind { get; }
	public static string FilePath { get; }
	public static bool IsAccess => Kind == DbKind.Access;

	static Db()
	{
		string baseDir = AppDomain.CurrentDomain.BaseDirectory;
		string sqlitePath = Path.Combine(baseDir, SQLiteFileName);
		if (File.Exists(sqlitePath))
		{
			Kind = DbKind.SQLite;
			FilePath = sqlitePath;
		}
		else
		{
			Kind = DbKind.Access;
			FilePath = Path.Combine(baseDir, AccessFileName);
		}
	}

	public static DbConnection OpenConn() =>
		IsAccess ? DbHelper.Access.OpenConn(FilePath) : DbHelper.SQLite.OpenConn(FilePath);

	public static DataTable GetDataTable(string sql, params object?[] args)
	{
		using var connection = OpenConn();
		using var command = CreateCommand(connection, sql, args);
		using DbDataAdapter adapter = IsAccess
			? new OleDbDataAdapter((OleDbCommand)command)
			: new SQLiteDataAdapter((SQLiteCommand)command);
		var table = new DataTable();
		adapter.Fill(table);
		return table;
	}

	public static List<Dictionary<string, object?>> GetDictionary(string sql, params object?[] args) =>
		GetDataTable(sql, args).ToDictionary();

	public static int ExecuteNonQuery(string sql, params object?[] args)
	{
		using var connection = OpenConn();
		using var command = CreateCommand(connection, sql, args);
		return command.ExecuteNonQuery();
	}

	public static List<string> GetTableList() =>
		IsAccess ? DbHelper.Access.GetDBTableList(FilePath) : DbHelper.SQLite.GetDBTableList(FilePath);

	public static void Compact()
	{
		if (IsAccess) DbHelper.Access.CompactAccessDB(FilePath);
		else DbHelper.SQLite.CompactSQLiteDB(FilePath);
	}

	private static DbCommand CreateCommand(DbConnection connection, string sql, object?[] args)
	{
		var command = connection.CreateCommand();
		command.CommandText = sql;
		foreach (var arg in args)
		{
			var parameter = command.CreateParameter();
			parameter.Value = arg ?? DBNull.Value;
			command.Parameters.Add(parameter);
		}
		return command;
	}
}
