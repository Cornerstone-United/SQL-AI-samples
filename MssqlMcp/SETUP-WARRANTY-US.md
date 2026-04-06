# MSSQL MCP Server Setup Guide — warranty_us

This guide walks through forking, building, and configuring the Microsoft SQL Server MCP (Model Context Protocol) server for use with the `warranty_us` database.

## Overview

The MCP server enables AI tools (Claude Code, VS Code Copilot, Claude Desktop) to interact directly with a SQL Server database through a standardized protocol. It exposes tools for listing tables, describing schemas, reading data, and (optionally) writing data.

**Source repo:** [Azure-Samples/SQL-AI-samples](https://github.com/Azure-Samples/SQL-AI-samples)
**Our fork:** [Cornerstone-United/SQL-AI-samples](https://github.com/Cornerstone-United/SQL-AI-samples)
**Implementation:** .NET 8 (C#), located in `MssqlMcp/dotnet/`

---

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) installed
- Access to a SQL Server instance with the `warranty_us` database
- [GitHub CLI (`gh`)](https://cli.github.com/) installed and authenticated
- [Claude Code CLI](https://docs.anthropic.com/en/docs/claude-code) installed (for Claude Code integration)

---

## Step 1: Fork the Repository (Already Done)

The repo has been forked to the Cornerstone-United GitHub org.

If you need to do this from scratch:

```bash
# Fork via GitHub CLI
gh repo fork Azure-Samples/SQL-AI-samples --org Cornerstone-United --clone=false

# Clone the fork
git clone https://github.com/Cornerstone-United/SQL-AI-samples.git
cd SQL-AI-samples

# Add the upstream remote (for pulling future updates from Microsoft)
git remote add upstream https://github.com/Azure-Samples/SQL-AI-samples.git
```

---

## Step 2: Clone the Repository

```bash
cd C:\Git
git clone https://github.com/Cornerstone-United/SQL-AI-samples.git
cd SQL-AI-samples
```

Verify the remotes:

```bash
git remote -v
# origin    https://github.com/Cornerstone-United/SQL-AI-samples.git (fetch)
# origin    https://github.com/Cornerstone-United/SQL-AI-samples.git (push)
# upstream  https://github.com/Azure-Samples/SQL-AI-samples.git (fetch)
# upstream  https://github.com/Azure-Samples/SQL-AI-samples.git (push)
```

---

## Step 3: Build the .NET MCP Server

```bash
cd MssqlMcp/dotnet/MssqlMcp
dotnet build
```

Expected output:

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

The compiled executable will be at:

```
C:\Git\SQL-AI-samples\MssqlMcp\dotnet\MssqlMcp\bin\Debug\net8.0\MssqlMcp.exe
```

---

## Step 4: Run Unit Tests (Optional)

The tests are integration tests that require a live SQL Server connection. To run them:

```bash
# Set the connection string for the test database
set CONNECTION_STRING=Server=SQL-DEV02.CSU.LOCAL;Database=warranty_us;Trusted_Connection=True;TrustServerCertificate=True

# Run from the solution directory
cd MssqlMcp/dotnet
dotnet test
```

> **Note:** Tests will fail without the `CONNECTION_STRING` environment variable set and a reachable SQL Server instance. This is expected.

---

## Step 5: Register the MCP Server

### Option A: Claude Code CLI

```bash
claude mcp add --transport stdio "warranty-us-mssql" \
  -e CONNECTION_STRING="Server=SQL-DEV02.CSU.LOCAL;Database=warranty_us;Trusted_Connection=True;TrustServerCertificate=True" \
  -e READONLY="true" \
  -e MAX_RESULT_SET="500" \
  -- "C:\Git\SQL-AI-samples\MssqlMcp\dotnet\MssqlMcp\bin\Debug\net8.0\MssqlMcp.exe"
```

Verify it's connected:

```bash
claude mcp list
# warranty-us-mssql: ... - ✓ Connected
```

### Option B: VS Code Settings (settings.json)

Press `Ctrl+Shift+P` > "Preferences: Open Settings (JSON)" and add:

```json
"mcp": {
    "servers": {
        "warranty-us-mssql": {
            "type": "stdio",
            "command": "C:\\Git\\SQL-AI-samples\\MssqlMcp\\dotnet\\MssqlMcp\\bin\\Debug\\net8.0\\MssqlMcp.exe",
            "env": {
                "CONNECTION_STRING": "Server=SQL-DEV02.CSU.LOCAL;Database=warranty_us;Trusted_Connection=True;TrustServerCertificate=True",
                "READONLY": "true",
                "MAX_RESULT_SET": "500"
            }
        }
    }
}
```

### Option C: Claude Desktop

File > Settings > Developer > Edit Config (`claude_desktop_config.json`):

```json
{
    "mcpServers": {
        "warranty-us-mssql": {
            "command": "C:\\Git\\SQL-AI-samples\\MssqlMcp\\dotnet\\MssqlMcp\\bin\\Debug\\net8.0\\MssqlMcp.exe",
            "env": {
                "CONNECTION_STRING": "Server=SQL-DEV02.CSU.LOCAL;Database=warranty_us;Trusted_Connection=True;TrustServerCertificate=True",
                "READONLY": "true",
                "MAX_RESULT_SET": "500"
            }
        }
    }
}
```

---

## Step 6: Verify It Works

In Claude Code or VS Code Agent Mode, try:

```
List all tables in the database
```

You should see the tables from `warranty_us` listed via the `ListTables` MCP tool.

---

## Environment Variables Reference

| Variable             | Required | Default | Description                                      |
|----------------------|----------|---------|--------------------------------------------------|
| `CONNECTION_STRING`  | Yes      | —       | Full SQL Server connection string                |
| `READONLY`           | No       | `false` | Set `true` to block write operations             |
| `MAX_RESULT_SET`     | No       | `100`   | Maximum rows returned by the ReadData tool       |

---

## Available MCP Tools

| Tool            | Description                              | Blocked in READONLY |
|-----------------|------------------------------------------|---------------------|
| `ListTables`    | List all tables in the database          | No                  |
| `DescribeTable` | Get schema/column details for a table    | No                  |
| `ReadData`      | Query data (SELECT only, injection-safe) | No                  |
| `CreateTable`   | Create a new table                       | Yes                 |
| `DropTable`     | Drop an existing table                   | Yes                 |
| `InsertData`    | Insert rows into a table                 | Yes                 |
| `UpdateData`    | Update rows in a table                   | Yes                 |

---

## Pulling Updates from Microsoft

To pull in upstream changes from the original Microsoft repo:

```bash
git fetch upstream
git checkout main
git merge upstream/main
git push origin main
```

---

## Security Notes

- **READONLY mode is enabled by default** in our config. Only disable it if you explicitly need write access.
- The `ReadData` tool has built-in SQL injection protection — queries must start with `SELECT` and blocked keywords (`DELETE`, `DROP`, `EXEC`, etc.) are rejected.
- **Do not commit connection strings** with passwords. Use Windows Authentication (`Trusted_Connection=True`) or manage credentials via environment variables.
- The connection string in this guide uses `TrustServerCertificate=True` which is suitable for local development. For production or remote servers, configure proper certificate validation.

---

## Troubleshooting

| Issue | Solution |
|-------|---------|
| MCP server shows "Needs authentication" | Verify the `CONNECTION_STRING` env var is set correctly |
| Tests fail with "Connection string is not set" | Set `CONNECTION_STRING` env var before running `dotnet test` |
| "Task canceled" error with Azure AD auth | Switch from "Active Directory Default" to "Active Directory Interactive" in the connection string |
| MCP server not appearing in Claude Code | Restart Claude Code after running `claude mcp add` |
| Build fails | Ensure .NET 8 SDK is installed: `dotnet --version` should show `8.x.x` |
