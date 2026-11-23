using System.Text.Json;
using UAI.ZeroVP.DTO;
using static UAI.ZeroVP.Tests.DTO.ValidationHelper;

namespace UAI.ZeroVP.Tests.DTO;

/// <summary>Validační pravidla požadavku na založení úkolu (POST /api/todos).</summary>
public sealed class CreateTodoItemRequestTests
{
    private const string ValidUserId = "jan.novak";

    /// <summary>Stejné nastavení jako ve webovém API (camelCase názvy vlastností).</summary>
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Validate_RequiredFieldsOnly_IsValid()
    {
        var request = new CreateTodoItemRequest { Title = "Napsat Dockerfile", UserId = ValidUserId };

        Assert.Empty(Validate(request));
    }

    [Fact]
    public void Priority_WhenNotSet_DefaultsToNormal()
    {
        var request = new CreateTodoItemRequest { Title = "Úkol" };

        Assert.Equal(TodoPriority.Normal, request.Priority);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")] // [Required] bez AllowEmptyStrings odmítne i samé mezery
    public void Validate_MissingTitle_FailsOnTitle(string? title)
    {
        var request = new CreateTodoItemRequest { Title = title!, UserId = ValidUserId };

        AssertSingleErrorFor(request, nameof(CreateTodoItemRequest.Title));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(200)]
    public void Validate_TitleWithinLimit_IsValid(int length)
    {
        var request = new CreateTodoItemRequest { Title = new string('a', length), UserId = ValidUserId };

        Assert.Empty(Validate(request));
    }

    [Fact]
    public void Validate_TitleOverLimit_FailsOnTitle()
    {
        var request = new CreateTodoItemRequest { Title = new string('a', 201), UserId = ValidUserId };

        AssertSingleErrorFor(request, nameof(CreateTodoItemRequest.Title));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("krátký popis")]
    public void Validate_OptionalDescription_IsValid(string? description)
    {
        var request = new CreateTodoItemRequest { Title = "Úkol", Description = description, UserId = ValidUserId };

        Assert.Empty(Validate(request));
    }

    [Fact]
    public void Validate_DescriptionAtLimit_IsValid()
    {
        var request = new CreateTodoItemRequest { Title = "Úkol", Description = new string('a', 1000), UserId = ValidUserId };

        Assert.Empty(Validate(request));
    }

    [Fact]
    public void Validate_DescriptionOverLimit_FailsOnDescription()
    {
        var request = new CreateTodoItemRequest { Title = "Úkol", Description = new string('a', 1001), UserId = ValidUserId };

        AssertSingleErrorFor(request, nameof(CreateTodoItemRequest.Description));
    }

    [Theory]
    [InlineData(TodoPriority.Low)]
    [InlineData(TodoPriority.Normal)]
    [InlineData(TodoPriority.High)]
    public void Validate_DefinedPriority_IsValid(TodoPriority priority)
    {
        var request = new CreateTodoItemRequest { Title = "Úkol", Priority = priority, UserId = ValidUserId };

        Assert.Empty(Validate(request));
    }

    [Fact]
    public void Validate_UndefinedPriority_FailsOnPriority()
    {
        // Číslo mimo výčet – JSON deserializace by ho jako číslo propustila.
        var request = new CreateTodoItemRequest { Title = "Úkol", Priority = (TodoPriority)99, UserId = ValidUserId };

        AssertSingleErrorFor(request, nameof(CreateTodoItemRequest.Priority));
    }

    // AC 2, 5: chybějící, null, prázdný i jen z mezer složený userId validace odmítne.
    // Požadavek vzniká z JSON stejně jako v API – jen tak se do vlastnosti typu string dostane null.
    [Theory]
    [InlineData("""{"title":"Úkol"}""")]
    [InlineData("""{"title":"Úkol","userId":null}""")]
    [InlineData("""{"title":"Úkol","userId":""}""")]
    [InlineData("""{"title":"Úkol","userId":"   "}""")]
    public void Validate_MissingUserId_FailsOnUserId(string json)
    {
        var request = Assert.IsType<CreateTodoItemRequest>(
            JsonSerializer.Deserialize<CreateTodoItemRequest>(json, WebOptions));

        AssertSingleErrorFor(request, nameof(CreateTodoItemRequest.UserId));
    }

    // AC 5: userId o délce 1 i 100 znaků projde.
    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void Validate_UserIdWithinLimit_IsValid(int length)
    {
        var request = new CreateTodoItemRequest { Title = "Úkol", UserId = new string('u', length) };

        Assert.Empty(Validate(request));
    }

    // AC 2, 5: userId delší než 100 znaků validace odmítne.
    [Fact]
    public void Validate_UserIdOverLimit_FailsOnUserId()
    {
        var request = new CreateTodoItemRequest { Title = "Úkol", UserId = new string('u', 101) };

        AssertSingleErrorFor(request, nameof(CreateTodoItemRequest.UserId));
    }
}
