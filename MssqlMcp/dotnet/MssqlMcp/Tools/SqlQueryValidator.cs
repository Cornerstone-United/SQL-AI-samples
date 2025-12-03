// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.

using System.Text.RegularExpressions;

namespace Mssql.McpServer;

/// <summary>
/// Validates SQL queries to ensure only safe SELECT operations are executed.
/// Ported from Node version (ReadDataTool.ts) for security parity.
/// </summary>
public static partial class SqlQueryValidator
{
    public const int MaxQueryLength = 10000;
    public const int MaxRecordCount = 10000;

    /// <summary>
    /// Dangerous SQL keywords that should not be allowed in read-only queries.
    /// </summary>
    private static readonly string[] DangerousKeywords =
    [
        "DELETE", "DROP", "UPDATE", "INSERT", "ALTER", "CREATE",
        "TRUNCATE", "EXEC", "EXECUTE", "MERGE", "REPLACE",
        "GRANT", "REVOKE", "COMMIT", "ROLLBACK", "TRANSACTION",
        "BEGIN", "DECLARE", "SET", "USE", "BACKUP",
        "RESTORE", "KILL", "SHUTDOWN", "WAITFOR", "OPENROWSET",
        "OPENDATASOURCE", "OPENQUERY", "OPENXML", "BULK", "INTO"
    ];

    /// <summary>
    /// Regex patterns to detect common SQL injection techniques.
    /// </summary>
    private static readonly Regex[] DangerousPatterns =
    [
        // SELECT INTO operations that create new tables
        SelectIntoPattern(),
        // Semicolon followed by dangerous keywords
        SemicolonDangerousPattern(),
        // UNION injection attempts with dangerous keywords
        UnionInjectionPattern(),
        // Comment-based injection attempts (line comments)
        LineCommentInjectionPattern(),
        // Comment-based injection attempts (block comments)
        BlockCommentInjectionPattern(),
        // Stored procedure execution patterns
        ExecParenPattern(),
        ExecuteParenPattern(),
        SpPrefixPattern(),
        XpPrefixPattern(),
        // Bulk operations
        BulkInsertPattern(),
        OpenRowsetPattern(),
        OpenDatasourcePattern(),
        // System functions that could be dangerous
        SystemVariablePattern(),
        SystemUserPattern(),
        UserNamePattern(),
        DbNamePattern(),
        HostNamePattern(),
        // Time delay attacks
        WaitforDelayPattern(),
        WaitforTimePattern(),
        // Multiple statements (semicolon not at end)
        MultipleStatementsPattern(),
        // String concatenation that might hide malicious code
        CharConcatPattern(),
        NcharConcatPattern(),
        AsciiConcatPattern()
    ];

    // Generated regex patterns using source generators for performance
    [GeneratedRegex(@"SELECT\s+.*?\s+INTO\s+", RegexOptions.IgnoreCase)]
    private static partial Regex SelectIntoPattern();

    [GeneratedRegex(@";\s*(DELETE|DROP|UPDATE|INSERT|ALTER|CREATE|TRUNCATE|EXEC|EXECUTE|MERGE|REPLACE|GRANT|REVOKE)", RegexOptions.IgnoreCase)]
    private static partial Regex SemicolonDangerousPattern();

    [GeneratedRegex(@"UNION\s+(?:ALL\s+)?SELECT.*?(DELETE|DROP|UPDATE|INSERT|ALTER|CREATE|TRUNCATE|EXEC|EXECUTE)", RegexOptions.IgnoreCase)]
    private static partial Regex UnionInjectionPattern();

    [GeneratedRegex(@"--.*?(DELETE|DROP|UPDATE|INSERT|ALTER|CREATE|TRUNCATE|EXEC|EXECUTE)", RegexOptions.IgnoreCase)]
    private static partial Regex LineCommentInjectionPattern();

    [GeneratedRegex(@"/\*.*?(DELETE|DROP|UPDATE|INSERT|ALTER|CREATE|TRUNCATE|EXEC|EXECUTE).*?\*/", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex BlockCommentInjectionPattern();

    [GeneratedRegex(@"EXEC\s*\(", RegexOptions.IgnoreCase)]
    private static partial Regex ExecParenPattern();

    [GeneratedRegex(@"EXECUTE\s*\(", RegexOptions.IgnoreCase)]
    private static partial Regex ExecuteParenPattern();

    [GeneratedRegex(@"\bsp_", RegexOptions.IgnoreCase)]
    private static partial Regex SpPrefixPattern();

    [GeneratedRegex(@"\bxp_", RegexOptions.IgnoreCase)]
    private static partial Regex XpPrefixPattern();

    [GeneratedRegex(@"BULK\s+INSERT", RegexOptions.IgnoreCase)]
    private static partial Regex BulkInsertPattern();

    [GeneratedRegex(@"OPENROWSET", RegexOptions.IgnoreCase)]
    private static partial Regex OpenRowsetPattern();

    [GeneratedRegex(@"OPENDATASOURCE", RegexOptions.IgnoreCase)]
    private static partial Regex OpenDatasourcePattern();

    [GeneratedRegex(@"@@")]
    private static partial Regex SystemVariablePattern();

    [GeneratedRegex(@"SYSTEM_USER", RegexOptions.IgnoreCase)]
    private static partial Regex SystemUserPattern();

    [GeneratedRegex(@"USER_NAME", RegexOptions.IgnoreCase)]
    private static partial Regex UserNamePattern();

    [GeneratedRegex(@"DB_NAME", RegexOptions.IgnoreCase)]
    private static partial Regex DbNamePattern();

    [GeneratedRegex(@"HOST_NAME", RegexOptions.IgnoreCase)]
    private static partial Regex HostNamePattern();

    [GeneratedRegex(@"WAITFOR\s+DELAY", RegexOptions.IgnoreCase)]
    private static partial Regex WaitforDelayPattern();

    [GeneratedRegex(@"WAITFOR\s+TIME", RegexOptions.IgnoreCase)]
    private static partial Regex WaitforTimePattern();

    [GeneratedRegex(@";\s*\w")]
    private static partial Regex MultipleStatementsPattern();

    [GeneratedRegex(@"\+\s*CHAR\s*\(", RegexOptions.IgnoreCase)]
    private static partial Regex CharConcatPattern();

    [GeneratedRegex(@"\+\s*NCHAR\s*\(", RegexOptions.IgnoreCase)]
    private static partial Regex NcharConcatPattern();

    [GeneratedRegex(@"\+\s*ASCII\s*\(", RegexOptions.IgnoreCase)]
    private static partial Regex AsciiConcatPattern();

    // Patterns for cleaning queries
    [GeneratedRegex(@"--.*$", RegexOptions.Multiline)]
    private static partial Regex LineCommentPattern();

    [GeneratedRegex(@"/\*[\s\S]*?\*/")]
    private static partial Regex BlockCommentPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();

    /// <summary>
    /// Validates a SQL query for security issues.
    /// </summary>
    /// <param name="query">The SQL query to validate.</param>
    /// <returns>A validation result indicating success or failure with error message.</returns>
    public static (bool IsValid, string? Error) ValidateQuery(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return (false, "Query must be a non-empty string");
        }

        // Check query length
        if (query.Length > MaxQueryLength)
        {
            return (false, $"Query is too long. Maximum allowed length is {MaxQueryLength:N0} characters.");
        }

        // Remove comments and normalize whitespace for analysis
        var cleanQuery = LineCommentPattern().Replace(query, "");
        cleanQuery = BlockCommentPattern().Replace(cleanQuery, "");
        cleanQuery = WhitespacePattern().Replace(cleanQuery, " ").Trim();

        if (string.IsNullOrWhiteSpace(cleanQuery))
        {
            return (false, "Query cannot be empty after removing comments");
        }

        // Must start with SELECT
        if (!cleanQuery.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
        {
            return (false, "Query must start with SELECT for security reasons");
        }

        // Check for dangerous keywords using word boundaries
        foreach (var keyword in DangerousKeywords)
        {
            var pattern = $@"(^|\s|[^A-Za-z0-9_]){keyword}($|\s|[^A-Za-z0-9_])";
            if (Regex.IsMatch(cleanQuery, pattern, RegexOptions.IgnoreCase))
            {
                return (false, $"Dangerous keyword '{keyword}' detected in query. Only SELECT operations are allowed.");
            }
        }

        // Check for dangerous patterns
        foreach (var pattern in DangerousPatterns)
        {
            if (pattern.IsMatch(query))
            {
                return (false, "Potentially malicious SQL pattern detected. Only simple SELECT queries are allowed.");
            }
        }

        // Check for multiple statements
        var statements = cleanQuery.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
        if (statements.Count > 1)
        {
            return (false, "Multiple SQL statements are not allowed. Use only a single SELECT statement.");
        }

        // Check for character conversion functions (obfuscation)
        if (query.Contains("CHAR(", StringComparison.OrdinalIgnoreCase) ||
            query.Contains("NCHAR(", StringComparison.OrdinalIgnoreCase) ||
            query.Contains("ASCII(", StringComparison.OrdinalIgnoreCase))
        {
            return (false, "Character conversion functions are not allowed as they may be used for obfuscation.");
        }

        return (true, null);
    }
}
