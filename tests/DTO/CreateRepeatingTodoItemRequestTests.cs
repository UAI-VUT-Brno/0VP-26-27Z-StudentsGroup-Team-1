using UAI.ZeroVP.DTO;
using static UAI.ZeroVP.Tests.DTO.ValidationHelper;

namespace UAI.ZeroVP.Tests.DTO;

/// <summary>
/// Validační pravidla požadavku na týdně se opakující úkol (POST /api/todos/repeat).
/// Pravidla pro název, popis a prioritu musí odpovídat <see cref="CreateTodoItemRequest"/>.
/// </summary>
public sealed class CreateRepeatingTodoItemRequestTests
{
    private static readonly DateTimeOffset FirstDue = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

    private static CreateRepeatingTodoItemRequest CreateValidRequest() => new()
    {
        Title = "Úkol",
        UserId = "honza"
    };

    [Fact]
    public void Validate_RequiredFieldsOnly_IsValid()
    {
        Assert.Empty(Validate(CreateValidRequest()));
    }

    [Fact]
    public void Priority_WhenNotSet_DefaultsToNormal()
    {
        Assert.Equal(TodoPriority.Normal, CreateValidRequest().Priority);
    }

    // --- Pole úkolu (stejná pravidla jako POST /api/todos) ------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("x" + "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
    public void Validate_InvalidUserId_FailsOnUserId(string? userId)
    {
        var request = CreateValidRequest() with { UserId = userId! };

        AssertSingleErrorFor(request, nameof(CreateTodoItemRequest.UserId));
    }

    [Fact]
    public void Validate_UserId_Over100Characters_FailsOnUserId()
    {
        var request = CreateValidRequest() with { UserId = new string('x', 101) };

        AssertSingleErrorFor(request, nameof(CreateTodoItemRequest.UserId));
    }

    [Fact]
    public void Validate_UserId_At100Characters_IsValid()
    {
        var request = CreateValidRequest() with { UserId = new string('x', 100) };

        Assert.Empty(Validate(request));
    }

    [Fact]
    public void Validate_UserId_IsTrimmed()
    {
        var request = CreateValidRequest() with { UserId = "  honza  " };

        Assert.Equal("honza", request.UserId);
        Assert.Empty(Validate(request));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_MissingTitle_FailsOnTitle(string? title)
    {
        var request = CreateValidRequest() with { Title = title! };

        AssertSingleErrorFor(request, nameof(CreateRepeatingTodoItemRequest.Title));
    }

    [Fact]
    public void Validate_TitleOverLimit_FailsOnTitle()
    {
        var request = CreateValidRequest() with { Title = new string('a', 201) };

        AssertSingleErrorFor(request, nameof(CreateRepeatingTodoItemRequest.Title));
    }

    [Fact]
    public void Validate_DescriptionOverLimit_FailsOnDescription()
    {
        var request = CreateValidRequest() with { Description = new string('a', 1001) };

        AssertSingleErrorFor(request, nameof(CreateRepeatingTodoItemRequest.Description));
    }

    [Fact]
    public void Validate_UndefinedPriority_FailsOnPriority()
    {
        var request = CreateValidRequest() with { Priority = (TodoPriority)99 };

        AssertSingleErrorFor(request, nameof(CreateRepeatingTodoItemRequest.Priority));
    }

    // --- Termín prvního výskytu ---------------------------------------------

    [Fact]
    public void Validate_MissingFirstDueDate_FailsOnFirstDueDate()
    {
        var request = CreateValidRequest() with { FirstDueAtUtc = null };

        AssertSingleErrorFor(request, nameof(CreateRepeatingTodoItemRequest.FirstDueAtUtc));
    }

    [Fact]
    public void Validate_FirstDueDateInThePast_IsValid()
    {
        // Zadání termín v minulosti nezakazuje – lze tak dohnat zpětně i rozjetou řadu.
        var request = CreateValidRequest() with { FirstDueAtUtc = FirstDue.AddYears(-1) };

        Assert.Empty(Validate(request));
    }

    [Fact]
    public void Validate_LastOccurrenceAfterYear9999_FailsOnFirstDueDate()
    {
        // Bez této kontroly by výpočet termínů v kontroleru spadl a API vrátilo 500.
        var request = CreateValidRequest() with
        {
            FirstDueAtUtc = DateTimeOffset.MaxValue.AddDays(-20),
            RepeatCount = 3
        };

        AssertSingleErrorFor(request, nameof(CreateRepeatingTodoItemRequest.FirstDueAtUtc));
    }

    [Fact]
    public void Validate_LastOccurrenceExactlyAtMaxDate_IsValid()
    {
        var request = CreateValidRequest() with
        {
            FirstDueAtUtc = DateTimeOffset.MaxValue.AddDays(-21),
            RepeatCount = 3
        };

        Assert.Empty(Validate(request));
    }

    // --- Počet opakování ----------------------------------------------------

    [Theory]
    [InlineData(1)]
    [InlineData(26)]
    [InlineData(52)]
    public void Validate_RepeatCountWithinRange_IsValid(int repeatCount)
    {
        var request = CreateValidRequest() with { RepeatCount = repeatCount };

        Assert.Empty(Validate(request));
    }

    [Theory]
    [InlineData(0)] // i chybějící hodnota v JSON – int má výchozí hodnotu 0
    [InlineData(-1)]
    [InlineData(53)]
    [InlineData(int.MaxValue)]
    public void Validate_RepeatCountOutOfRange_FailsOnRepeatCount(int repeatCount)
    {
        var request = CreateValidRequest() with { RepeatCount = repeatCount };

        AssertSingleErrorFor(request, nameof(CreateRepeatingTodoItemRequest.RepeatCount));
    }
}