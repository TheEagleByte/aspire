// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ModelContextProtocol.Protocol;

namespace Aspire.Dashboard.Mcp;

public static class McpExtensions
{
    public static IMcpServerBuilder AddAspireMcpTools(this IServiceCollection services)
    {
        var builder = services.AddMcpServer(options =>
        {
            options.ServerInfo = new Implementation { Name = "Aspire MCP Server", Version = "1.0.0" };
            options.ServerInstructions =
            """
                This MCP Server provides various tools for managing Aspire resources.
                When a resource is mentioned, use its name with bold chars like **resourceName**.
                Add an icon based on the resource `Type` property. Suggest the next actions based on the MCP tools that take a `resourceName` as an argument.                
            """;
        }).WithHttpTransport();

        builder.WithTools<ResourceMcpTools>();

        return builder;
    }
}
