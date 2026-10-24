using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace UltimateKtv;

public static class DataTableExtension
{
	public static DataTable GetPagedTable(this DataTable dt, int PageIndex, int PageSize)
	{
		// A page index of 0 is treated as a special case to return the full table.
		// Returning a copy via dt.Copy() is safer than returning the original dt instance.
		if (PageIndex == 0)
		{
			return dt.Copy();
		}

		// Clone the structure of the original table, but not the data.
		using DataTable pagedTable = dt.Clone();

		// LINQ's Skip/Take is efficient and handles all edge cases gracefully
		// (e.g., empty source, page index past the end).
		var pagedRows = dt.AsEnumerable()
						  .Skip((PageIndex - 1) * PageSize)
						  .Take(PageSize);

		// Import the selected rows into the new paged table.
		foreach (DataRow row in pagedRows)
		{
			pagedTable.ImportRow(row);
		}
		return pagedTable;
	}

	public static List<Dictionary<string, object?>> ToDictionary(this DataTable dt)
	{
		// Using Parallel.ForEach with a static lock is inefficient for this kind of workload.
		// A standard LINQ Select is cleaner, more readable, and often faster.
		return dt.AsEnumerable().Select(row =>
			// For each row, create a dictionary. The value can be DBNull, so the
			// dictionary's value type must be nullable (object?).
			dt.Columns.Cast<DataColumn>().ToDictionary(
				col => col.ColumnName,
				col => row[col] == DBNull.Value ? null : row[col])
		).ToList();
	}

	public static DataRow ToDataRow(this Dictionary<string, object?> dict, DataTable dt)
	{
		var row = dt.NewRow();
		foreach (var col in dt.Columns.Cast<DataColumn>())
		{
			if (dict.TryGetValue(col.ColumnName, out var value))
			{
				row[col.ColumnName] = value ?? DBNull.Value;
			}
		}
		return row;
	}
}
