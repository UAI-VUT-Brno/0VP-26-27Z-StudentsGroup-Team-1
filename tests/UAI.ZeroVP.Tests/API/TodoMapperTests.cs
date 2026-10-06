using UAI.ZeroVP.API.Mapping;
using UAI.ZeroVP.Data.Entities;
using UAI.ZeroVP.DTO;

namespace UAI.ZeroVP.Tests.API;

/// <summary>
/// Převody mezi entitou a DTO. Kromě kopírování hodnot mapper normalizuje vstup
/// (ořezání mezer, prázdný popis → <c>null</c>) a hlídá čas dokončení úkolu.
/// </summary>
public sealed class TodoMapperTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 9, 2, 10, 0, 0, TimeSpan.Zero);

    private static TodoItem CreateEntity(bool isCompleted = false) => new()
    {
        Id = Guid.NewGuid(),
        Title = "Napsat Dockerfile",
        Description = "Vícefázový build.",
        Priority = TodoPriority.High,
        IsCompleted = isCompleted,
        CreatedAtUtc = CreatedAt,
        CompletedAtUtc = isCompleted ? CreatedAt.AddHours(1) : null,
        UpdatedAtUtc = CreatedAt.AddHours(1)
    };

    // --- ToDto --------------------------------------------------------------

    [Fact]
    public void ToDto_CopiesAllFields()
    {
        var entity = CreateEntity(isCompleted: true);

        var dto = entity.ToDto();

        Assert.Equal(entity.Id, dto.Id);
        Assert.Equal(entity.Title, dto.Title);
        Assert.Equal(entity.Description, dto.Description);
        Assert.Equal(entity.Priority, dto.Priority);
        Assert.Equal(entity.IsCompleted, dto.IsCompleted);
        Assert.Equal(entity.CreatedAtUtc, dto.CreatedAtUtc);
        Assert.Equal(entity.CompletedAtUtc, dto.CompletedAtUtc);
    }

    [Fact]
    public void ToDto_MapsEveryDtoPropertyFromSameNamedEntityProperty()
    {
        // Pojistka pro budoucí změny: přibude-li do DTO vlastnost, kterou mapper
        // nevyplní, zůstane ve výstupu API výchozí hodnota a tento test selže.
        var entity = CreateEntity(isCompleted: true);

        var dto = entity.ToDto();

        foreach (var dtoProperty in typeof(TodoItemDto).GetProperties())
        {
            var entityProperty = typeof(TodoItem).GetProperty(dtoProperty.Name);
            Assert.NotNull(entityProperty);
            Assert.Equal(entityProperty.GetValue(entity), dtoProperty.GetValue(dto));
        }
    }

    [Fact]
    public void ToDto_DoesNotExposeInternalAuditField()
    {
        // UpdatedAtUtc je interní – hlavní důvod, proč DTO existuje jako samostatná třída.
        Assert.Null(typeof(TodoItemDto).GetProperty(nameof(TodoItem.UpdatedAtUtc)));
    }

    // --- ToEntity -----------------------------------------------------------

    [Fact]
    public void ToEntity_CreatesNewUncompletedItemStampedWithGivenTime()
    {
        var request = new CreateTodoItemRequest { Title = "Úkol", Priority = TodoPriority.Low };

        var entity = request.ToEntity(Now);

        Assert.NotEqual(Guid.Empty, entity.Id);
        Assert.Equal(TodoPriority.Low, entity.Priority);
        Assert.False(entity.IsCompleted);
        Assert.Null(entity.CompletedAtUtc);
        Assert.Equal(Now, entity.CreatedAtUtc);
        Assert.Equal(Now, entity.UpdatedAtUtc);
    }

    [Fact]
    public void ToEntity_EachCallGeneratesDifferentId()
    {
        var request = new CreateTodoItemRequest { Title = "Úkol" };

        Assert.NotEqual(request.ToEntity(Now).Id, request.ToEntity(Now).Id);
    }

    [Fact]
    public void ToEntity_TrimsTitleAndDescription()
    {
        var request = new CreateTodoItemRequest { Title = "  Úkol  ", Description = "  Popis  " };

        var entity = request.ToEntity(Now);

        Assert.Equal("Úkol", entity.Title);
        Assert.Equal("Popis", entity.Description);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ToEntity_BlankDescription_BecomesNull(string? description)
    {
        var request = new CreateTodoItemRequest { Title = "Úkol", Description = description };

        Assert.Null(request.ToEntity(Now).Description);
    }

    // --- ApplyTo ------------------------------------------------------------

    [Fact]
    public void ApplyTo_OverwritesEditableFieldsAndNormalizesThem()
    {
        var entity = CreateEntity();
        var request = new UpdateTodoItemRequest
        {
            Title = "  Nový název  ",
            Description = "  Nový popis  ",
            Priority = TodoPriority.Low
        };

        request.ApplyTo(entity, Now);

        Assert.Equal("Nový název", entity.Title);
        Assert.Equal("Nový popis", entity.Description);
        Assert.Equal(TodoPriority.Low, entity.Priority);
        Assert.Equal(Now, entity.UpdatedAtUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ApplyTo_BlankDescription_ClearsDescription(string? description)
    {
        var entity = CreateEntity();

        new UpdateTodoItemRequest { Title = "Úkol", Description = description }.ApplyTo(entity, Now);

        Assert.Null(entity.Description);
    }

    [Fact]
    public void ApplyTo_KeepsIdentityAndCreationTime()
    {
        var entity = CreateEntity();
        var originalId = entity.Id;

        new UpdateTodoItemRequest { Title = "Úkol" }.ApplyTo(entity, Now);

        Assert.Equal(originalId, entity.Id);
        Assert.Equal(CreatedAt, entity.CreatedAtUtc);
    }

    [Fact]
    public void ApplyTo_CompletedFlag_SetsCompletionTime()
    {
        var entity = CreateEntity(isCompleted: false);

        new UpdateTodoItemRequest { Title = "Úkol", IsCompleted = true }.ApplyTo(entity, Now);

        Assert.True(entity.IsCompleted);
        Assert.Equal(Now, entity.CompletedAtUtc);
    }

    // --- SetCompletionState -------------------------------------------------

    [Fact]
    public void SetCompletionState_CompletingOpenItem_SetsCompletionTimeToNow()
    {
        var entity = CreateEntity(isCompleted: false);

        entity.SetCompletionState(isCompleted: true, Now);

        Assert.True(entity.IsCompleted);
        Assert.Equal(Now, entity.CompletedAtUtc);
    }

    [Fact]
    public void SetCompletionState_CompletingAlreadyCompletedItem_KeepsOriginalCompletionTime()
    {
        // Opakované POST /complete (nebo PUT s IsCompleted = true) nesmí přepsat,
        // kdy byl úkol skutečně dokončen.
        var entity = CreateEntity(isCompleted: true);
        var originalCompletedAt = entity.CompletedAtUtc;

        entity.SetCompletionState(isCompleted: true, Now);

        Assert.Equal(originalCompletedAt, entity.CompletedAtUtc);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SetCompletionState_Reopening_ClearsCompletionTime(bool wasCompleted)
    {
        var entity = CreateEntity(isCompleted: wasCompleted);

        entity.SetCompletionState(isCompleted: false, Now);

        Assert.False(entity.IsCompleted);
        Assert.Null(entity.CompletedAtUtc);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void SetCompletionState_AnyTransition_UpdatesAuditTime(bool wasCompleted, bool isCompleted)
    {
        var entity = CreateEntity(wasCompleted);

        entity.SetCompletionState(isCompleted, Now);

        Assert.Equal(Now, entity.UpdatedAtUtc);
    }
}
