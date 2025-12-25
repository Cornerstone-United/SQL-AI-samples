// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.

using System.ComponentModel;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Mssql.McpServer;
public partial class Tools
{
    [McpServerTool(
        Title = "Read Data",
        ReadOnly = true,
        Idempotent = true,
        Destructive = false),
        Description("Executes a SELECT query on an SQL Database table. The query must start with SELECT (or WITH for CTEs) and cannot contain any destructive SQL operations for security reasons.")]
    public async Task<DbOperationResult> ReadData(
        [Description("SQL SELECT query to execute (must start with SELECT or WITH for CTEs, and cannot contain destructive operations)")] string sql)
    {
        // Validate the query for security issues
        var (isValid, validationError) = SqlQueryValidator.ValidateQuery(sql);
        if (!isValid)
        {
            _logger.LogWarning("Security validation failed for query: {Query}", sql[..Math.Min(sql.Length, 100)]);
            return new DbOperationResult(success: false, error: $"Security validation failed: {validationError}");
        }

        var conn = await _connectionFactory.GetOpenConnectionAsync();
        try
        {
            using (conn)
            {
                using var cmd = new SqlCommand(sql, conn);
                using var reader = await cmd.ExecuteReaderAsync();
                var results = new List<Dictionary<string, object?>>();
                var totalRecords = 0;
                while (await reader.ReadAsync())
                {
                    totalRecords++;
                    // Limit results to prevent memory issues
                    if (results.Count < SqlQueryValidator.MaxRecordCount)
                    {
                        var row = new Dictionary<string, object?>();
                        for (var i = 0; i < reader.FieldCount; i++)
                        {
                            row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                        }
                        results.Add(row);
                    }
                }

                var message = totalRecords > SqlQueryValidator.MaxRecordCount
                    ? $"Query returned {totalRecords:N0} records, limited to {SqlQueryValidator.MaxRecordCount:N0}"
                    : null;

                return new DbOperationResult(success: true, data: results, message: message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ReadData failed: {Message}", ex.Message);
            // Don't expose internal error details to prevent information leakage
            var safeError = ex.Message.Contains("Invalid object name", StringComparison.OrdinalIgnoreCase)
                ? ex.Message
                : "Database query execution failed";
            return new DbOperationResult(success: false, error: safeError);
        }
    }
}
