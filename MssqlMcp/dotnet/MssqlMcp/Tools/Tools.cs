// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.

using System.Data;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Mssql.McpServer;

// Register this class as a tool container
[McpServerToolType]
public partial class Tools(ISqlConnectionFactory connectionFactory, ILogger<Tools> logger)
{
    private readonly ISqlConnectionFactory _connectionFactory = connectionFactory;
    private readonly ILogger<Tools> _logger = logger;

    /// <summary>
    /// Gets whether the server is running in read-only mode.
    /// Set READONLY=true environment variable to enable.
    /// </summary>
    public static bool IsReadOnly { get; } =
        string.Equals(Environment.GetEnvironmentVariable("READONLY"), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns an error result if read-only mode is enabled.
    /// </summary>
    private static DbOperationResult? CheckReadOnlyMode()
    {
        return IsReadOnly
            ? new DbOperationResult(success: false, error: "This operation is not allowed in read-only mode. Set READONLY=false to enable write operations.")
            : null;
    }

    // Helper to convert DataTable to a serializable list
    private static List<Dictionary<string, object>> DataTableToList(DataTable table)
    {
        var result = new List<Dictionary<string, object>>();
        foreach (DataRow row in table.Rows)
        {
            var dict = new Dictionary<string, object>();
            foreach (DataColumn col in table.Columns)
            {
                dict[col.ColumnName] = row[col];
            }
            result.Add(dict);
        }
        return result;
    }
}