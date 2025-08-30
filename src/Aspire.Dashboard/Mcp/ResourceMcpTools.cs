// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using Aspire.Dashboard.Model;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Aspire.Dashboard.Mcp;

[McpServerToolType]
internal sealed class ResourceMcpTools
{

    [McpServerTool, Description("Lists resources with their name, type and state.")]
    public static async Task<object> ListResources(IDashboardClient dashboardClient)
    {
        var resources = new List<object>();
        try
        {
            var cts = new CancellationTokenSource(millisecondsDelay: 500);
            var subscription = await dashboardClient.SubscribeResourcesAsync(cts.Token).ConfigureAwait(false);
            foreach (var resource in subscription.InitialState)
            {
                resources.Add(new { resource.Name, resource.ResourceType, resource.State });
            }
        }
        catch { }
        return resources;
    }

    [McpServerTool, Description("Returns the console logs for a resource.")]
    public static async Task<object> GetResourceConsoleLogs(
        IDashboardClient dashboardClient,
        [Description("The name of the resource")] string resourceName,
        [Description("The maximum number of log lines to return")] int maxLines = 200)
    {
        var resource = dashboardClient.GetResource(resourceName);
        if (resource == null)
        {
            throw new McpException($"Resource '{resourceName}' not found.", McpErrorCode.InvalidParams);
        }
        var logs = new List<object>();
        try
        {
            var cts = new CancellationTokenSource(millisecondsDelay: 1000);
            await foreach (var batch in dashboardClient.SubscribeConsoleLogs(resourceName, cts.Token).ConfigureAwait(false))
            {
                foreach (var log in batch)
                {
                    logs.Add(new { log.LineNumber, log.Content, log.IsErrorMessage });
                    if (logs.Count >= maxLines)
                    {
                        return logs;
                    }
                }
            }
        }
        catch { }
        return logs;
    }

    [McpServerTool, Description("Returns the structured logs for a resource (not supported in this build).")]
    public static Task<object> GetResourceStructuredLogs()
    {
        return Task.FromResult<object>(Array.Empty<object>());
    }

    [McpServerTool, Description("Returns the traces for a resource (not supported in this build).")]
    public static Task<object> GetResourceTraces()
    {
        return Task.FromResult<object>(Array.Empty<object>());
    }

    [McpServerTool, Description("Returns detailed information for a resource, including health checks, parent resource, and connection string.")]
    public static object GetResourceDetails(
        IDashboardClient dashboardClient,
        [Description("The name of the resource")] string resourceName)
    {
        var resource = dashboardClient.GetResource(resourceName);
        if (resource == null)
        {
            throw new McpException($"Resource '{resourceName}' not found.", McpErrorCode.InvalidParams);
        }
        return new
        {
            resource.Name,
            resource.ResourceType,
            resource.State,
            ParentResourceName = resource.GetResourcePropertyValue(KnownProperties.Resource.ParentName),
            ConnectionString = resource.GetResourcePropertyValue(KnownProperties.Resource.ConnectionString),
            HealthReports = resource.HealthReports.Select(h => new { h.Name, h.HealthStatus, h.Description, h.ExceptionText }).ToArray()
        };
    }

    [McpServerTool, Description("Lists the command names available for a resource.")]
    public static object GetResourceCommands(IDashboardClient dashboardClient, [Description("The name of the resource")] string resourceName)
    {
        var resource = dashboardClient.GetResource(resourceName);

        if (resource == null)
        {
            throw new McpException($"Resource '{resourceName}' not found.", McpErrorCode.InvalidParams);
        }

        // Only include commands that can be executed (Enabled).
        var commands = resource.Commands
            .Where(cmd => cmd.State == Model.CommandViewModelState.Enabled)
            .Select(cmd => new
            {
                cmd.Name
            });

        return commands;
    }

    [McpServerTool, Description("Executes a command on a resource.")]
    public static async Task ExecuteCommand(IDashboardClient dashboardClient, [Description("The name of the resource")] string resourceName, [Description("The name of the command")] string commandName)
    {
        var resource = dashboardClient.GetResource(resourceName);

        if (resource == null)
        {
            throw new McpException($"Resource '{resourceName}' not found.", McpErrorCode.InvalidParams);
        }

        var command = resource.Commands.FirstOrDefault(c => string.Equals(c.Name, commandName, StringComparison.Ordinal));

        if (command is null)
        {
            throw new McpException($"Command '{commandName}' not found for resource '{resourceName}'.", McpErrorCode.InvalidParams);
        }

        // Block execution when command isn't available.
        if (command.State == Model.CommandViewModelState.Hidden)
        {
            throw new McpException($"Command '{commandName}' is not available for resource '{resourceName}'.", McpErrorCode.InvalidParams);
        }

        if (command.State == Model.CommandViewModelState.Disabled)
        {
            throw new McpException($"Command '{commandName}' is currently disabled for resource '{resourceName}'.", McpErrorCode.InvalidParams);
        }

        try
        {
            var response = await dashboardClient.ExecuteResourceCommandAsync(resource.Name, resource.ResourceType, command, CancellationToken.None).ConfigureAwait(false);

            switch (response.Kind)
            {
                case Model.ResourceCommandResponseKind.Succeeded:
                    return;
                case Model.ResourceCommandResponseKind.Cancelled:
                    throw new McpException($"Command '{commandName}' was cancelled.", McpErrorCode.InternalError);
                case Model.ResourceCommandResponseKind.Failed:
                default:
                    var message = response.ErrorMessage is { Length: > 0 } ? response.ErrorMessage : "Unknown error. See logs for details.";
                    throw new McpException($"Command '{commandName}' failed for resource '{resourceName}': {message}", McpErrorCode.InternalError);
            }
        }
        catch (McpException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new McpException($"Error executing command '{commandName}' for resource '{resourceName}': {ex.Message}", McpErrorCode.InternalError);
        }
    }
}
