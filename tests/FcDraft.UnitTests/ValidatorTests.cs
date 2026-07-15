using FcDraft.Application.Features.Auth;
using Xunit;

namespace FcDraft.UnitTests;

public sealed class ValidatorTests
{
    private readonly LoginCommandValidator _login = new();
    private readonly ChangePasswordCommandValidator _change = new();

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
}
