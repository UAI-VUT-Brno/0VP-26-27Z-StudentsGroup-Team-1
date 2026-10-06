using UAI.ZeroVP.DTO;
using static UAI.ZeroVP.Tests.DTO.ValidationHelper;

namespace UAI.ZeroVP.Tests.DTO;

/// <summary>
/// Validační pravidla požadavku na úplnou aktualizaci úkolu (PUT /api/todos/{id}).
/// Pravidla pro název, popis a prioritu musí odpovídat <see cref="CreateTodoItemRequest"/>.
/// </summary>
public sealed class UpdateTodoItemRequestTests
{
    [Fact]
    public void Validate_TitleOnly_IsValid()
    {
        var request = new UpdateTodoItemRequest { Title = "Napsat Dockerfile" };

        Assert.Empty(Validate(request));
    }

    [Fact]
    public void Defaults_WhenNotSet_AreNormalPriorityAndNotCompleted()
    {
        // Full update: neposlané hodnoty se přepíší právě těmito výchozími.
        var request = new UpdateTodoItemRequest { Title = "Úkol" };

        Assert.Equal(TodoPriority.Normal, request.Priority);
        Assert.False(request.IsCompleted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_MissingTitle_FailsOnTitle(string? title)
    {
        var request = new UpdateTodoItemRequest { Title = title! };

        AssertSingleErrorFor(request, nameof(UpdateTodoItemRequest.Title));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(200)]
    public void Validate_TitleWithinLimit_IsValid(int length)
    {
        var request = new UpdateTodoItemRequest { Title = new string('a', length) };

        Assert.Empty(Validate(request));
    }

    [Fact]
    public void Validate_TitleOverLimit_FailsOnTitle()
    {
        var request = new UpdateTodoItemRequest { Title = new string('a', 201) };

        AssertSingleErrorFor(request, nameof(UpdateTodoItemRequest.Title));
    }

    [Fact]
    public void Validate_DescriptionAtLimit_IsValid()
    {
        var request = new UpdateTodoItemRequest { Title = "Úkol", Description = new string('a', 1000) };

        Assert.Empty(Validate(request));
    }

    [Fact]
    public void Validate_DescriptionOverLimit_FailsOnDescription()
    {
        var request = new UpdateTodoItemRequest { Title = "Úkol", Description = new string('a', 1001) };

        AssertSingleErrorFor(request, nameof(UpdateTodoItemRequest.Description));
    }

    [Fact]
    public void Validate_UndefinedPriority_FailsOnPriority()
    {
        var request = new UpdateTodoItemRequest { Title = "Úkol", Priority = (TodoPriority)99 };

        AssertSingleErrorFor(request, nameof(UpdateTodoItemRequest.Priority));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Validate_AnyCompletionState_IsValid(bool isCompleted)
    {
        var request = new UpdateTodoItemRequest { Title = "Úkol", IsCompleted = isCompleted };

        Assert.Empty(Validate(request));
    }
}
