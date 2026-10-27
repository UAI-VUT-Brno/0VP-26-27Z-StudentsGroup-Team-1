
using UAI.ZeroVP.DTO;
using UAI.ZeroVP.API.Scheduling;
using UAI.ZeroVP.Data.Entities;
namespace UAI.ZeroVP.API.Mapping;

/// <summary>
/// Převody mezi entitou datové vrstvy a přenosovými objekty API.
/// Mapování patří do API vrstvy – ta jediná zná obě strany.
/// </summary>
public static class TodoMapper
{
    /// <summary>Převede entitu na výstupní DTO (interní pole se zahodí).</summary>
    public static TodoItemDto ToDto(this TodoItem entity) => new()
    {
        Id = entity.Id,
        UserId = entity.UserId,
        Title = entity.Title,
        Description = entity.Description,
        Priority = entity.Priority,
        IsCompleted = entity.IsCompleted,
        DueAtUtc = entity.DueAtUtc,
        CreatedAtUtc = entity.CreatedAtUtc,
        CompletedAtUtc = entity.CompletedAtUtc
    };

    /// <summary>Vytvoří novou entitu z požadavku na založení úkolu.</summary>
    /// <param name="request">Data z těla požadavku.</param>
    /// <param name="nowUtc">Aktuální čas – předává se zvenčí kvůli testovatelnosti.</param>
    public static TodoItem ToEntity(this CreateTodoItemRequest request, DateTimeOffset nowUtc) => new()
    {
        Id = Guid.NewGuid(),
        UserId = request.UserId.Trim(),
        Title = request.Title.Trim(),
        Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
        Priority = request.Priority,
        IsCompleted = false,
        DueAtUtc = null,
        CreatedAtUtc = nowUtc,
        CompletedAtUtc = null,
        UpdatedAtUtc = nowUtc
    };

    /// <summary>
    /// Vytvoří entitu pro každý výskyt týdně se opakujícího úkolu.
    /// Výskyty sdílejí uživatele, název, popis a prioritu, ale každý má vlastní <c>Id</c> a termín.
    /// </summary>
    /// <param name="request">Data z těla požadavku (už zvalidovaná).</param>
    /// <param name="nowUtc">Aktuální čas – předává se zvenčí kvůli testovatelnosti.</param>
    public static IReadOnlyList<TodoItem> ToEntities(this CreateRepeatingTodoItemRequest request, DateTimeOffset nowUtc)
    {
        var firstDueAtUtc = request.FirstDueAtUtc
            ?? throw new ArgumentException("Termín prvního výskytu chybí.", nameof(request));

        var template = new CreateTodoItemRequest
        {
            UserId = request.UserId,
            Title = request.Title,
            Description = request.Description,
            Priority = request.Priority
        };

        return WeeklySchedule.GetDueDates(firstDueAtUtc, request.RepeatCount)
            .Select(dueAtUtc =>
            {
                var entity = template.ToEntity(nowUtc);
                entity.DueAtUtc = dueAtUtc;
                return entity;
            })
            .ToList();
    }

    /// <summary>
    /// Promítne data z požadavku do existující entity. Uživatel se nemění.
    /// Hlídá i přechod stavu dokončení, aby <c>CompletedAtUtc</c> odpovídal příznaku.
    /// </summary>
    public static void ApplyTo(this UpdateTodoItemRequest request, TodoItem entity, DateTimeOffset nowUtc)
    {
        entity.Title = request.Title.Trim();
        entity.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        entity.Priority = request.Priority;
        entity.SetCompletionState(request.IsCompleted, nowUtc);
        entity.UpdatedAtUtc = nowUtc;
    }

    /// <summary>Nastaví příznak dokončení a dopočítá čas dokončení.</summary>
    public static void SetCompletionState(this TodoItem entity, bool isCompleted, DateTimeOffset nowUtc)
    {
        if (isCompleted && !entity.IsCompleted)
        {
            entity.CompletedAtUtc = nowUtc;
        }
        else if (!isCompleted)
        {
            entity.CompletedAtUtc = null;
        }

        entity.IsCompleted = isCompleted;
        entity.UpdatedAtUtc = nowUtc;
    }
}
