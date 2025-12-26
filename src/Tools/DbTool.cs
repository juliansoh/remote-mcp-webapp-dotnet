using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Text.Json;
using Azure.Identity;
using Azure.Core;

namespace McpServer.Tools;


[McpServerToolType]
public class SqlCrudTools
{
    private static string _sqlServerConnectionString = "";
    private static readonly string _sqlScope = "https://database.windows.net/.default";

    // Configure once at startup
    public static void Configure(string connectionString)
    {
        _sqlServerConnectionString = connectionString;
    }

    // Acquire an Entra token for SQL
    private static async Task<string> GetAccessTokenAsync()
    {
        var credential = new DefaultAzureCredential();
        AccessToken token = await credential.GetTokenAsync(
            new TokenRequestContext(new[] { _sqlScope })
        );
        return token.Token;
    }

    private static async Task<SqlConnection> CreateConnectionAsync()
    {
        var conn = new SqlConnection(_sqlServerConnectionString);
        conn.AccessToken = await GetAccessTokenAsync();
        await conn.OpenAsync();
        return conn;
    }

    // -----------------------------
    // CREATE
    // -----------------------------
    [McpServerTool, Description("Insert a new record into a SQL table using Entra authentication.")]
    public static async Task<string> CreateRecord(
        [Description("Table name.")] string table,
        [Description("JSON object of column:value pairs.")] string jsonData)
    {
        try
        {
            var data = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonData)
                       ?? new Dictionary<string, object>();

            var columns = string.Join(",", data.Keys);
            var parameters = string.Join(",", data.Keys.Select(k => "@" + k));

            string sql = $@"
                INSERT INTO {table} ({columns})
                VALUES ({parameters});
                SELECT SCOPE_IDENTITY();
            ";

            using var conn = await CreateConnectionAsync();
            using var cmd = new SqlCommand(sql, conn);

            foreach (var kv in data)
                cmd.Parameters.AddWithValue("@" + kv.Key, kv.Value ?? DBNull.Value);

            var result = await cmd.ExecuteScalarAsync();
            return $"Inserted record with ID: {result}";
        }
        catch (Exception ex)
        {
            return $"Error inserting record: {ex.Message}";
        }
    }

    // -----------------------------
    // READ
    // -----------------------------
    [McpServerTool, Description("Read a record from a SQL table using Entra authentication.")]
    public static async Task<string> ReadRecord(
        [Description("Table name.")] string table,
        [Description("Primary key column name.")] string keyColumn,
        [Description("Record ID.")] string id)
    {
        try
        {
            string sql = $"SELECT * FROM {table} WHERE {keyColumn} = @id";

            using var conn = await CreateConnectionAsync();
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", id);

            using var reader = await cmd.ExecuteReaderAsync();

            if (!reader.Read())
                return "Record not found.";

            return RowToJson(reader);
        }
        catch (Exception ex)
        {
            return $"Error reading record: {ex.Message}";
        }
    }

    // -----------------------------
    // UPDATE
    // -----------------------------
    [McpServerTool, Description("Update a record in a SQL table using Entra authentication.")]
    public static async Task<string> UpdateRecord(
        [Description("Table name.")] string table,
        [Description("Primary key column name.")] string keyColumn,
        [Description("Record ID.")] string id,
        [Description("JSON object of column:value pairs to update.")] string jsonData)
    {
        try
        {
            var data = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonData)
                       ?? new Dictionary<string, object>();

            var setClause = string.Join(",", data.Keys.Select(k => $"{k}=@{k}"));

            string sql = $@"
                UPDATE {table}
                SET {setClause}
                WHERE {keyColumn} = @id
            ";

            using var conn = await CreateConnectionAsync();
            using var cmd = new SqlCommand(sql, conn);

            foreach (var kv in data)
                cmd.Parameters.AddWithValue("@" + kv.Key, kv.Value ?? DBNull.Value);

            cmd.Parameters.AddWithValue("@id", id);

            int rows = await cmd.ExecuteNonQueryAsync();

            return rows > 0
                ? "Record updated successfully."
                : "No record updated.";
        }
        catch (Exception ex)
        {
            return $"Error updating record: {ex.Message}";
        }
    }

    // -----------------------------
    // DELETE
    // -----------------------------
    [McpServerTool, Description("Delete a record from a SQL table using Entra authentication.")]
    public static async Task<string> DeleteRecord(
        [Description("Table name.")] string table,
        [Description("Primary key column name.")] string keyColumn,
        [Description("Record ID.")] string id)
    {
        try
        {
            string sql = $"DELETE FROM {table} WHERE {keyColumn} = @id";

            using var conn = await CreateConnectionAsync();
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", id);

            int rows = await cmd.ExecuteNonQueryAsync();

            return rows > 0
                ? "Record deleted successfully."
                : "No record deleted.";
        }
        catch (Exception ex)
        {
            return $"Error deleting record: {ex.Message}";
        }
    }

    // -----------------------------
    // COUNT RECORDS
    // -----------------------------
    [McpServerTool, Description("Count the number of records in a SQL table using Entra authentication.")]
    public static async Task<string> CountRecords(
        [Description("Table name (include schema if needed, e.g., 'SalesLT.Product').")] string table)
    {
        try
        {
            string sql = $"SELECT COUNT(*) FROM {table}";

            using var conn = await CreateConnectionAsync();
            using var cmd = new SqlCommand(sql, conn);

            var result = await cmd.ExecuteScalarAsync();
            return $"Table '{table}' contains {result} records.";
        }
        catch (Exception ex)
        {
            return $"Error counting records: {ex.Message}";
        }
    }

    // -----------------------------
    // LIST TABLES
    // -----------------------------
    [McpServerTool, Description("List all tables in the SQL database using Entra authentication.")]
    public static async Task<string> ListTables()
    {
        try
        {
            string sql = @"
                SELECT 
                    TABLE_SCHEMA,
                    TABLE_NAME,
                    TABLE_TYPE
                FROM INFORMATION_SCHEMA.TABLES 
                WHERE TABLE_TYPE = 'BASE TABLE'
                ORDER BY TABLE_SCHEMA, TABLE_NAME
            ";

            using var conn = await CreateConnectionAsync();
            using var cmd = new SqlCommand(sql, conn);
            using var reader = await cmd.ExecuteReaderAsync();

            var tables = new List<object>();
            while (await reader.ReadAsync())
            {
                tables.Add(new
                {
                    Schema = reader.GetString("TABLE_SCHEMA"),
                    TableName = reader.GetString("TABLE_NAME"),
                    TableType = reader.GetString("TABLE_TYPE")
                });
            }

            return JsonSerializer.Serialize(tables, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return $"Error listing tables: {ex.Message}";
        }
    }



    // -----------------------------
    // EXECUTE QUERY
    // -----------------------------
    [McpServerTool, Description("Execute a custom SQL SELECT query using Entra authentication.")]
    public static async Task<string> ExecuteQuery(
        [Description("SQL SELECT query to execute.")] string sqlQuery)
    {
        try
        {
            using var conn = await CreateConnectionAsync();
            using var cmd = new SqlCommand(sqlQuery, conn);
            using var reader = await cmd.ExecuteReaderAsync();

            var results = new List<Dictionary<string, object>>();
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }
                results.Add(row);
            }

            return JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return $"Error executing query: {ex.Message}";
        }
    }

    // -----------------------------
    // Helper: Convert row to JSON
    // -----------------------------
    private static string RowToJson(SqlDataReader reader)
    {
        var dict = new Dictionary<string, object>();

        for (int i = 0; i < reader.FieldCount; i++)
        {
            dict[reader.GetName(i)] = reader.IsDBNull(i)
                ? null
                : reader.GetValue(i);
        }

        return JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
    }
}
