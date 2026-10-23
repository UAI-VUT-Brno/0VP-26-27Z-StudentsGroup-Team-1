using UAI.ZeroVP.Data.Entities;
using UAI.ZeroVP.DTO;

namespace UAI.ZeroVP.Tests.Data;

/// <summary>
/// Klonování entity. Na <see cref="TodoItem.Clone"/> stojí in-memory repozitář:
/// ukládá i vrací jen kopie, aby volající nemohl měnit uložený stav mimo něj.
/// </summary>
public sealed class TodoItemTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

    /// <summary>Entita, jejíž každá vlastnost má jinou než výchozí hodnotu.</summary>
    private static TodoItem CreateFullyPopulatedItem() => new()
    {
        Id = Guid.NewGuid(),
        Title = "Napsat Dockerfile",
        Description = "Vícefázový build.",
        Priority = TodoPriority.High,
        IsCompleted = true,
        DueAtUtc = CreatedAt.AddDays(7),
        CreatedAtUtc = CreatedAt,
        CompletedAtUtc = CreatedAt.AddHours(2),
        UpdatedAtUtc = CreatedAt.AddHours(3)
    };

    [Fact]
    public void Clone_CopiesAllProperties()
    {
        var original = CreateFullyPopulatedItem();

        var clone = original.Clone();

        Assert.Equal(original.Id, clone.Id);
        Assert.Equal(original.Title, clone.Title);
        Assert.Equal(original.Description, clone.Description);
        Assert.Equal(original.Priority, clone.Priority);
        Assert.Equal(original.IsCompleted, clone.IsCompleted);
        Assert.Equal(original.DueAtUtc, clone.DueAtUtc);
        Assert.Equal(original.CreatedAtUtc, clone.CreatedAtUtc);
        Assert.Equal(original.CompletedAtUtc, clone.CompletedAtUtc);
        Assert.Equal(original.UpdatedAtUtc, clone.UpdatedAtUtc);
    }

    [Fact]
    public void Clone_CopiesEveryPublicProperty()
    {
        // Pojistka pro budoucí změny: přibude-li do entity nová vlastnost,
        // test selže, dokud ji nedoplníme do CreateFullyPopulatedItem i do Clone().
        var original = CreateFullyPopulatedItem();

        var clone = original.Clone();

        foreach (var property in typeof(TodoItem).GetProperties())
        {
            var originalValue = property.GetValue(original);
            var defaultValue = property.PropertyType.IsValueType
                ? Activator.CreateInstance(property.PropertyType)
                : null;

            Assert.NotEqual(defaultValue, originalValue);
            Assert.Equal(originalValue, property.GetValue(clone));
        }
    }

    [Fact]
    public void Clone_ReturnsNewInstance()
    {
        var original = CreateFullyPopulatedItem();

        var clone = original.Clone();

        Assert.NotSame(original, clone);
    }

    [Fact]
    public void Clone_ModifyingClone_DoesNotChangeOriginal()
    {
        var original = CreateFullyPopulatedItem();
        var clone = original.Clone();

        clone.Title = "Změněný název";
        clone.Description = null;
        clone.Priority = TodoPriority.Low;
        clone.IsCompleted = false;
        clone.DueAtUtc = null;
        clone.CompletedAtUtc = null;
        clone.UpdatedAtUtc = CreatedAt.AddDays(1);

        Assert.Equal("Napsat Dockerfile", original.Title);
        Assert.Equal("Vícefázový build.", original.Description);
        Assert.Equal(TodoPriority.High, original.Priority);
        Assert.True(original.IsCompleted);
        Assert.Equal(CreatedAt.AddDays(7), original.DueAtUtc);
        Assert.Equal(CreatedAt.AddHours(2), original.CompletedAtUtc);
        Assert.Equal(CreatedAt.AddHours(3), original.UpdatedAtUtc);
    }

    [Fact]
    public void Clone_NullOptionalFields_StayNull()
    {
        var original = new TodoItem
        {
            Id = Guid.NewGuid(),
            Title = "Nedokončený úkol",
            CreatedAtUtc = CreatedAt
        };

        var clone = original.Clone();

        Assert.Null(clone.Description);
        Assert.Null(clone.DueAtUtc);
        Assert.Null(clone.CompletedAtUtc);
    }
}
