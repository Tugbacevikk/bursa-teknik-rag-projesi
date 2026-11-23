using BtuAsistan.Api.Modules.Chat;
using BtuAsistan.Api.Modules.Identity;
using BtuAsistan.Api.Modules.Search;

namespace BtuAsistan.Api.Tests.Unit;

public sealed class ChatRequestTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_question_is_refused(string? question)
    {
        Assert.Contains("question", new ChatRequest(question, null).Validate().Keys);
    }

    [Fact]
    public void Question_may_be_as_long_as_the_limit_but_no_longer()
    {
        Assert.Empty(new ChatRequest(new string('a', SearchOptions.MaxQuestionLength), null).Validate());
        Assert.NotEmpty(new ChatRequest(new string('a', SearchOptions.MaxQuestionLength + 1), null).Validate());
    }

    // Surrounding spaces do not count towards the limit.
    [Fact]
    public void Length_is_counted_without_surrounding_spaces()
    {
        Assert.Empty(new ChatRequest($"  {new string('a', SearchOptions.MaxQuestionLength)}  ", null).Validate());
    }
}

// PLANNING D2.
public sealed class MessageFeedbackRequestTests
{
    [Theory]
    [InlineData("up", MessageFeedback.Up)]
    [InlineData("Up", MessageFeedback.Up)]
    [InlineData("DOWN", MessageFeedback.Down)]
    public void Rating_is_read_in_any_case(string rating, MessageFeedback expected)
    {
        Assert.Empty(new MessageFeedbackRequest(rating, null).Validate(out var feedback));
        Assert.Equal(expected, feedback);
    }

    // Enum.TryParse alone would take numbers.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("like")]
    public void Anything_else_is_refused(string? rating)
    {
        Assert.Contains("rating", new MessageFeedbackRequest(rating, null).Validate(out _).Keys);
    }

    [Fact]
    public void Comment_may_be_up_to_500_characters()
    {
        Assert.Empty(new MessageFeedbackRequest("down", new string('x', MessageFeedbackRequest.MaxCommentLength)).Validate(out _));
        Assert.Contains("comment", new MessageFeedbackRequest("down", new string('x', MessageFeedbackRequest.MaxCommentLength + 1)).Validate(out _).Keys);
    }
}

// PLANNING D6.
public sealed class UserRequestTests
{
    [Theory]
    [InlineData("ad.soyad@btu.edu.tr", "Editor", UserRole.Editor)]
    [InlineData("  ad.soyad@btu.edu.tr ", "admin", UserRole.Admin)]
    public void Valid_account_request(string email, string role, UserRole expected)
    {
        Assert.Empty(new CreateUserRequest(email, role).Validate(out var parsedEmail, out var parsedRole));
        Assert.Equal("ad.soyad@btu.edu.tr", parsedEmail);
        Assert.Equal(expected, parsedRole);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("adsoyad")]
    [InlineData("Ad Soyad <ad@btu.edu.tr>")]
    [InlineData("ad@")]
    public void Invalid_email_is_refused(string? email)
    {
        Assert.Contains("email", new CreateUserRequest(email, "Editor").Validate(out _, out _).Keys);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Root")]
    [InlineData("1")]
    public void Unknown_role_is_refused(string? role)
    {
        Assert.Contains("role", new CreateUserRequest("a@btu.edu.tr", role).Validate(out _, out _).Keys);
    }

    [Fact]
    public void Change_with_nothing_to_change_is_refused()
    {
        Assert.NotEmpty(new UpdateUserRequest(null, null).Validate(out _));
    }

    [Fact]
    public void Change_of_state_only_leaves_the_role()
    {
        Assert.Empty(new UpdateUserRequest(null, false).Validate(out var role));
        Assert.Null(role);
    }

    [Fact]
    public void Change_to_an_unknown_role_is_refused()
    {
        Assert.Contains("role", new UpdateUserRequest("Owner", null).Validate(out _).Keys);
    }
}
