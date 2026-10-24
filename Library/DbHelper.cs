using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Data.OleDb;
using System.Data.SQLite;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace UltimateKtv;

public class DbHelper
{
	public class Access
	{
		private static string OleDbProvider => Environment.Is64BitProcess ? "Microsoft.ACE.OLEDB.12.0" : "Microsoft.Jet.OLEDB.4.0";

		public static OleDbConnection OpenConn(string Database, string? Password= null)
		{
			// Force the OLE DB provider to release any pooled connections.
			// This can resolve "file already in use" errors by clearing lingering locks.
			OleDbConnection.ReleaseObjectPool();
			// Using a connection string builder is more robust and readable than manual concatenation.
			var builder = new OleDbConnectionStringBuilder
			{
				Provider = OleDbProvider,
				DataSource = Database
			};

			// Explicitly set the mode to allow shared read/write access. This often resolves "Operation must use an updateable query" errors.
			builder["Mode"] = "Share Deny None";

			if (!string.IsNullOrEmpty(Password))
			{
				builder["Jet OLEDB:Database Password"] = Password;
			}

			var connection = new OleDbConnection(builder.ConnectionString);
			try
			{
				connection.Open();
			}
			catch (Exception ex)
			{
				// Log the error and re-throw to notify the caller of the failure.
				// Swallowing exceptions here is dangerous and hides problems.
				Console.WriteLine($"Failed to open Access connection: {ex.Message}");
				connection.Dispose(); // Clean up the failed connection object.
				throw;
			}
			return connection;
		}

		public static DataTable GetDataTable(string Database, string OleDbString, string? Password = null)
		{
			using OleDbConnection connection = OpenConn(Database, Password);
			using OleDbDataAdapter oleDbDataAdapter = new OleDbDataAdapter(OleDbString, connection);
			using DataSet dataSet = new DataSet();
			oleDbDataAdapter.Fill(dataSet);
			// Safely handle cases where the query returns no data tables to prevent an exception.
			if (dataSet.Tables.Count > 0)
			{
				return dataSet.Tables[0];
			}
			// Return a new, empty DataTable if no data was returned.
			return new DataTable();
		}

		public static List<Dictionary<string, object?>> GetDictionary(string Database, string OleDbString, string? Password = null)
		{
			using OleDbConnection selectConnection = OpenConn(Database, Password);
			using OleDbDataAdapter oleDbDataAdapter = new OleDbDataAdapter(OleDbString, selectConnection);
			using DataSet dataSet = new DataSet();
			oleDbDataAdapter.Fill(dataSet);
			// Safely handle cases where the query returns no data tables.
			if (dataSet.Tables.Count > 0)
			{
				return dataSet.Tables[0].ToDictionary();
			}
			return new List<Dictionary<string, object?>>();
		}

		public static List<string> GetDBTableList(string Database, string? Password = null)
		{
			var tableList = new List<string>();
			if (File.Exists(Database))
			{
				using OleDbConnection connection = OpenConn(Database, Password);
				using DataTable dataTable = connection.GetSchema("Tables");
				// The foreach loop handles an empty collection gracefully, so no need to check Rows.Count.
				foreach (DataRow item in dataTable.AsEnumerable())
				{
					// Added null-conditional operator for safety and removed duplicate code.
					if (item["TABLE_TYPE"]?.ToString() == "TABLE")
					{
						// Safely convert the table name to a string and add it to the list.
						var tableName = item["TABLE_NAME"]?.ToString();
						if (!string.IsNullOrEmpty(tableName))
						{
							tableList.Add(tableName);
						}
					}
				}
			}
			return tableList;
		}

		public static List<string> GetDBColumnList(string Database, string TableName, string? Password = null)
		{
			var columnList = new List<string>();
			using OleDbConnection connection = OpenConn(Database, Password);
			// The restriction array must be declared as nullable (string?[]) to allow null elements.
			using DataTable schema = connection.GetSchema("Columns", new string?[4] { null, null, TableName, null });
			foreach (DataRow row in schema.Rows)
			{
				// Safely convert the column name to a string and add it to the list.
				var columnName = row["COLUMN_NAME"]?.ToString();
				if (!string.IsNullOrEmpty(columnName))
				{
					columnList.Add(columnName);
				}
			}
			return columnList;
		}

		public static int ExecuteNonQuery(string Database, string OleDbString, string? Password = null, IEnumerable<OleDbParameter>? parameters = null)
		{
			// This method is for executing commands that don't return a result set (e.g., UPDATE, INSERT, DELETE).
			using OleDbConnection connection = OpenConn(Database, Password);
			using OleDbCommand command = new OleDbCommand(OleDbString, connection);
			
			// Add parameters to the command to prevent SQL injection.
			if (parameters != null)
			{
				foreach (var p in parameters)
				{
					// Ensure the parameter is not null before adding.
					if (p != null)
					{
						command.Parameters.Add(p);
					}
				}
			}
			
			return command.ExecuteNonQuery();
		}

		public static void CompactAccessDB(string databasePath, string? password = null)
		{
			// This method uses the Jet and Replication Objects (JRO) library via COM Interop.
			// It is fragile and requires the 32-bit Jet engine to be available.

			string passwordSegment = !string.IsNullOrEmpty(password) ? $"Jet OLEDB:Database Password={password};" : "";
			string sourceConnStr = $"Provider={OleDbProvider};Data Source={databasePath};{passwordSegment}";
			string? tempDbPath = Path.ChangeExtension(databasePath, ".tmp");
			if (string.IsNullOrEmpty(tempDbPath))
			{
				throw new ArgumentException("Invalid database path provided.", nameof(databasePath));
			}
			string destConnStr = $"Provider={OleDbProvider};Data Source={tempDbPath};Jet OLEDB:Engine Type=5";

			object? jro = null;
			try
			{
				// Late-bound call to the JRO COM object.
				Type? jroType = Type.GetTypeFromProgID("JRO.JetEngine");
				if (jroType is null)
				{
					throw new InvalidOperationException("Could not find the JRO.JetEngine COM object. Ensure the 32-bit Access Database Engine is installed.");
				}
				jro = Activator.CreateInstance(jroType);
				if (jro is null)
				{
					throw new InvalidOperationException("Failed to create an instance of JRO.JetEngine.");
				}
				object[] args = new object[] { sourceConnStr, destConnStr };
				jro.GetType().InvokeMember("CompactDatabase", BindingFlags.InvokeMethod, null, jro, args);

				// If compacting succeeds, replace the original file.
				File.Copy(tempDbPath, databasePath, overwrite: true);
			}
			catch (Exception ex)
			{
				// Log the error and re-throw. The caller needs to know that compacting failed.
				Console.WriteLine($"Failed to compact Access database: {ex.Message}");
				throw;
			}
			finally
			{
				// Ensure the temporary file is deleted and the COM object is released.
				if (File.Exists(tempDbPath))
				{
					File.Delete(tempDbPath);
				}
				if (jro != null)
				{
					Marshal.ReleaseComObject(jro);
				}
			}
		}
	}

	public class SQLite
	{
		public static SQLiteConnection OpenConn(string Database, string? Password = null)
		{
			// Using a connection string builder is a more robust and readable
			// way to construct connection strings than manual concatenation.
			var builder = new SQLiteConnectionStringBuilder
			{
				DataSource = Database,
				Version = 3,
				// Compress = true // This property may not exist in older package versions.
			};

			if (!string.IsNullOrEmpty(Password))
			{
				builder.Password = Password;
			}

			// Use the indexer to set the 'Compress' property for broader compatibility.
			builder["Compress"] = true;

			var connection = new SQLiteConnection(builder.ConnectionString);

			try
			{
                connection.Open();
			}
			catch (Exception ex)
            {
                // It's better to let the caller handle the exception or log it,
                // rather than swallowing it silently.
                Console.WriteLine($"Failed to open SQLite connection: {ex.Message}");
                connection.Dispose(); // Ensure connection is disposed on failure
                throw;
            }
			return connection;
		}

		public static DataTable GetDataTable(string Database, string SQLString, string? Password = null)
		{
			using var connection = OpenConn(Database, Password);
			using var adapter = new SQLiteDataAdapter(SQLString, connection);
			using var dataSet = new DataSet();
			adapter.Fill(dataSet);
			// Safely handle cases where the query returns no data tables to prevent an exception.
			if (dataSet.Tables.Count > 0)
			{
				return dataSet.Tables[0];
			}
			// Return a new, empty DataTable if no data was returned.
			return new DataTable();
		}

		public static List<Dictionary<string, object?>> GetDictionary(string Database, string SQLString, string? Password = null)
		{
			using var connection = OpenConn(Database, Password);
			using var adapter = new SQLiteDataAdapter(SQLString, connection);
			using var dataSet = new DataSet();
			adapter.Fill(dataSet);
			// Safely handle cases where the query returns no data tables.
			if (dataSet.Tables.Count > 0)
			{
				return dataSet.Tables[0].ToDictionary();
			}
			return new List<Dictionary<string, object?>>();
		}

		public static List<string> GetDBTableList(string Database, string? Password = null)
		{
			var tableList = new List<string>();
			if (File.Exists(Database))
			{
				using var connection = OpenConn(Database, Password);
				using DataTable schema = connection.GetSchema("Tables");
				foreach (DataRow row in schema.Rows)
                {
                    if (row["TABLE_TYPE"]?.ToString() == "TABLE")
                    {
						// Safely convert the table name to a string and add it to the list.
						var tableName = row["TABLE_NAME"]?.ToString();
						if (!string.IsNullOrEmpty(tableName))
						{
							tableList.Add(tableName);
						}
                    }
                }
			}
			return tableList;
		}

		public static List<string> GetDBColumnList(string Database, string TableName, string? Password = null)
		{
			var columnList = new List<string>();
			using var connection = OpenConn(Database, Password);
			// The restriction array must be declared as nullable (string?[]) to allow null elements.
			using DataTable schema = connection.GetSchema("Columns", new string?[4] { null, null, TableName, null });
			foreach (DataRow row in schema.Rows)
            {
				// Safely convert the column name to a string and add it to the list.
				var columnName = row["COLUMN_NAME"]?.ToString();
				if (!string.IsNullOrEmpty(columnName))
				{
					columnList.Add(columnName);
				}
            }
			return columnList;
		}

		public static void CompactSQLiteDB(string Database, string? Password = null)
		{
			if (!File.Exists(Database))
			{
				return;
			}
			using var connection = OpenConn(Database, Password);
			using var command = connection.CreateCommand();
			command.CommandText = "VACUUM;";
			command.ExecuteNonQuery();
		}
	}
}
