using FcDraft.Application.Features.Auth;
using FcDraft.Application.Features.Drafts;
using Xunit;

namespace FcDraft.UnitTests;

public sealed class ValidatorTests
{
    private readonly LoginCommandValidator _login = new();
    private readonly ChangePasswordCommandValidator _change = new();
    private readonly CreateDraftCommandValidator _createDraft = new();

    [Theory]
    [InlineData("user@draftroom.test", "any-password", true)]
    [InlineData("", "any-password", false)]
    [InlineData("not-an-email", "any-password", false)]
    [InlineData("user@draftroom.test", "", false)]
    public void Login_validation(string email, string password, bool expectedValid)
    {
        var result = _login.Validate(new LoginCommand(email, password));
        Assert.Equal(expectedValid, result.IsValid);
    }

    [Theory]
    [InlineData("Strong@2026Pass", "Strong@2026Pass", true)]   // meets every rule
    [InlineData("short1!A", "short1!A", false)]                 // too short (< 10)
    [InlineData("alllowercase1!", "alllowercase1!", false)]     // no uppercase
    [InlineData("ALLUPPERCASE1!", "ALLUPPERCASE1!", false)]     // no lowercase
    [InlineData("NoNumbersHere!", "NoNumbersHere!", false)]     // no digit
    [InlineData("NoSymbols20261", "NoSymbols20261", false)]     // no symbol
    public void New_password_complexity(string newPassword, string confirm, bool expectedValid)
    {
        var result = _change.Validate(new ChangePasswordCommand(
            "user@draftroom.test", "TempPass@123", newPassword, confirm));
        Assert.Equal(expectedValid, result.IsValid);
    }

    [Fact]
    public void New_password_must_match_confirmation()
    {
        var result = _change.Validate(new ChangePasswordCommand(
            "user@draftroom.test", "TempPass@123", "Strong@2026Pass", "Different@2026Pass"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Passwords do not match.");
    }

    [Fact]
    public void New_password_must_differ_from_the_temporary_password()
    {
        var result = _change.Validate(new ChangePasswordCommand(
            "user@draftroom.test", "Strong@2026Pass", "Strong@2026Pass", "Strong@2026Pass"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("different from the temporary password"));
    }

    [Theory]
    [InlineData("Friday Night", "1v1", true)]
    [InlineData("Weekend Cup", "2v2", true)]
    [InlineData("", "1v1", false)]              // name required
    [InlineData("Bad format", "3v3", false)]    // only 1v1 / 2v2
    [InlineData("Bad format", "", false)]
    public void Create_draft_validation(string name, string format, bool expectedValid)
    {
        var result = _createDraft.Validate(new CreateDraftCommand(name, format, Guid.NewGuid()));
        Assert.Equal(expectedValid, result.IsValid);
    }

    [Fact]
    public void Create_draft_requires_a_host()
    {
        var result = _createDraft.Validate(new CreateDraftCommand("Friday Night", "1v1", Guid.Empty));
        Assert.False(result.IsValid);
    }
}
