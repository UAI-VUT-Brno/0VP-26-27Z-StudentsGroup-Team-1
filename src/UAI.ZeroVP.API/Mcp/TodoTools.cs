using System.ComponentModel;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using UAI.ZeroVP.API.Mapping;
using UAI.ZeroVP.Data.Abstractions;

namespace UAI.ZeroVP.API.Mcp;

[McpServerToolType]
internal sealed class TodoTools(ITodoRepository repository, IOptions<JsonOptions> jsonOptions)
{
    [McpServerTool(Name = "list_todos", ReadOnly = true, Destructive = false)]
    [Description("Lists ToDo items, optionally filtered by completion status and user ID.")]
    public async Task<CallToolResult> ListAsync(
        CancellationToken cancellationToken,
        [Description("Filter by completion status. Omit to include both states.")] bool? isCompleted = null,
        [Description("Filter by user ID. Omit to include all users.")] string? userId = null)
    {
        // MVC converts a blank query string value to null for the REST endpoint.
        var items = await repository.GetAllAsync(
            userId: string.IsNullOrWhiteSpace(userId) ? null : userId,
            isCompleted: isCompleted,
            cancellationToken: cancellationToken);

        return JsonResult(items.Select(item => item.ToDto()).ToArray());
    }

    [McpServerTool(Name = "get_todo", ReadOnly = true, Destructive = false)]
    [Description("Gets a single ToDo item by its ID.")]
    public async Task<CallToolResult> GetAsync(
        [Description("The ToDo item's GUID.")] string id,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out var todoId))
        {
            return TextResult("The ToDo ID must be a valid GUID.", isError: true);
        }

        var item = await repository.GetByIdAsync(todoId, cancellationToken);
        return item is null
            ? TextResult("No ToDo item has the supplied ID.", isError: true)
            : JsonResult(item.ToDto());
    }

    private CallToolResult JsonResult<T>(T value) =>
        TextResult(JsonSerializer.Serialize(value, jsonOptions.Value.JsonSerializerOptions));

    private static CallToolResult TextResult(string text, bool isError = false) => new()
    {
        Content = [new TextContentBlock { Text = text }],
        IsError = isError
    };
}
