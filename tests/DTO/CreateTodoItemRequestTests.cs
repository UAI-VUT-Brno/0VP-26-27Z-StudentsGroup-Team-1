using UAI.ZeroVP.DTO;
using static UAI.ZeroVP.Tests.DTO.ValidationHelper;

namespace UAI.ZeroVP.Tests.DTO;

/// <summary>Validační pravidla požadavku na založení úkolu (POST /api/todos).</summary>
public sealed class CreateTodoItemRequestTests
{
    [Fact]
    public void Validate_UserIdAndTitleOnly_IsValid()
    {
        var request = new CreateTodoItemRequest { UserId = "jan.novak", Title = "Napsat Dockerfile" };

        Assert.Empty(Validate(request));
    }

    [Fact]
    public void Priority_WhenNotSet_DefaultsToNormal()
    {
        var request = new CreateTodoItemRequest { UserId = "jan.novak", Title = "Úkol" };

        Assert.Equal(TodoPriority.Normal, request.Priority);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")] // [Required] bez AllowEmptyStrings odmítne i samé mezery
    public void Validate_MissingTitle_FailsOnTitle(string? title)
    {
        var request = new CreateTodoItemRequest { UserId = "jan.novak", Title = title! };

        AssertSingleErrorFor(request, nameof(CreateTodoItemRequest.Title));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_MissingUserId_FailsOnUserId(string? userId)
    {
        var request = new CreateTodoItemRequest { UserId = userId!, Title = "Úkol" };

        AssertSingleErrorFor(request, nameof(CreateTodoItemRequest.UserId));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void Validate_UserIdWithinLimit_IsValid(int length)
    {
        var request = new CreateTodoItemRequest { UserId = new string('u', length), Title = "Úkol" };

        Assert.Empty(Validate(request));
    }

    [Fact]
    public void Validate_UserIdOverLimit_FailsOnUserId()
    {
        var request = new CreateTodoItemRequest { UserId = new string('u', 101), Title = "Úkol" };

        AssertSingleErrorFor(request, nameof(CreateTodoItemRequest.UserId));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(200)]
    public void Validate_TitleWithinLimit_IsValid(int length)
    {
        var request = new CreateTodoItemRequest { UserId = "jan.novak", Title = new string('a', length) };

        Assert.Empty(Validate(request));
    }

    [Fact]
    public void Validate_TitleOverLimit_FailsOnTitle()
    {
        var request = new CreateTodoItemRequest { UserId = "jan.novak", Title = new string('a', 201) };

        AssertSingleErrorFor(request, nameof(CreateTodoItemRequest.Title));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("krátký popis")]
    public void Validate_OptionalDescription_IsValid(string? description)
    {
        var request = new CreateTodoItemRequest { UserId = "jan.novak", Title = "Úkol", Description = description };

        Assert.Empty(Validate(request));
    }

    [Fact]
    public void Validate_DescriptionAtLimit_IsValid()
    {
        var request = new CreateTodoItemRequest { UserId = "jan.novak", Title = "Úkol", Description = new string('a', 1000) };

        Assert.Empty(Validate(request));
    }

    [Fact]
    public void Validate_DescriptionOverLimit_FailsOnDescription()
    {
        var request = new CreateTodoItemRequest { UserId = "jan.novak", Title = "Úkol", Description = new string('a', 1001) };

        AssertSingleErrorFor(request, nameof(CreateTodoItemRequest.Description));
    }

    [Theory]
    [InlineData(TodoPriority.Low)]
    [InlineData(TodoPriority.Normal)]
    [InlineData(TodoPriority.High)]
    public void Validate_DefinedPriority_IsValid(TodoPriority priority)
    {
        var request = new CreateTodoItemRequest { UserId = "jan.novak", Title = "Úkol", Priority = priority };

        Assert.Empty(Validate(request));
    }

    [Fact]
    public void Validate_UndefinedPriority_FailsOnPriority()
    {
        // Číslo mimo výčet – JSON deserializace by ho jako číslo propustila.
        var request = new CreateTodoItemRequest { UserId = "jan.novak", Title = "Úkol", Priority = (TodoPriority)99 };

        AssertSingleErrorFor(request, nameof(CreateTodoItemRequest.Priority));
    }
}
