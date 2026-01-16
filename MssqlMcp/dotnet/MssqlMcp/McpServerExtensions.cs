// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace Mssql.McpServer;

/// <summary>
/// Extension methods for IMcpServerBuilder to support filtered tool registration.
/// </summary>
public static class McpServerExtensions
{
    /// <summary>
    /// Registers tools from the current assembly, optionally filtering out write tools
    /// when running in read-only mode. In read-only mode, only tools marked with
    /// <see cref="McpServerToolAttribute.ReadOnly"/> = true are registered.
    /// </summary>
    /// <param name="builder">The MCP server builder.</param>
    /// <param name="readOnlyMode">When true, only tools with ReadOnly=true attribute are registered.</param>
    /// <param name="serializerOptions">Optional JSON serializer options.</param>
    /// <returns>The builder for chaining.</returns>
    [RequiresUnreferencedCode("Tool discovery uses reflection to find and analyze tool methods.")]
    public static IMcpServerBuilder WithToolsFromAssemblyFiltered(
        this IMcpServerBuilder builder,
        bool readOnlyMode = false,
        JsonSerializerOptions? serializerOptions = null)
    {
        var assembly = Assembly.GetExecutingAssembly();

        // Find all types with [McpServerToolType]
        var toolTypes = assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() != null)
            .ToList();

        if (!readOnlyMode)
        {
            // When not in readonly mode, register all tools normally
            return builder.WithTools(toolTypes, serializerOptions);
        }

        // In readonly mode, we need to filter tools.
        // Build a list of McpServerTool instances, filtering by ReadOnly attribute.
        var filteredTools = new List<McpServerTool>();
        var createOptions = serializerOptions != null
            ? new McpServerToolCreateOptions { SerializerOptions = serializerOptions }
            : null;

        foreach (var toolType in toolTypes)
        {
            var methods = toolType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() != null);

            foreach (var method in methods)
            {
                var attr = method.GetCustomAttribute<McpServerToolAttribute>()!;

                // Skip write tools (ReadOnly = false) when in readonly mode
                if (!attr.ReadOnly)
                    continue;

                // Create McpServerTool from method using SDK factory
                // Use the Type overload for instance methods to enable DI
                var tool = McpServerTool.Create(method, toolType, createOptions);
                filteredTools.Add(tool);
            }
        }

        return builder.WithTools(filteredTools);
    }
}
