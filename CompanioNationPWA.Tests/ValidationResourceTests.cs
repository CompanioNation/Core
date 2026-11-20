using System.ComponentModel.DataAnnotations;
using CompanioNationPWA.Pages;

namespace CompanioNationPWA.Tests;

/// <summary>
/// Regression tests for the DataAnnotation validation messages on the public forms.
/// DataAnnotations resolves <c>ErrorMessageResourceType</c> by reflection for a STATIC PROPERTY;
/// when the type has none it throws "The resource type '…' does not have an accessible static
/// property named '…'" instead of showing the localized message. That crashed the client on /Link
/// in production (26.11.15.23 — CompanioNationPWA.SharedStrings, Validation_InvalidEmailFormat).
/// </summary>
public class ValidationResourceTests
{
    [Fact]
    public void ValidEmailProducesNoValidationError()
    {
        var model = new Link.EmailLinkModel { EmailAddress = "someone@example.com" };

        Assert.Empty(ValidateProperty(model, nameof(Link.EmailLinkModel.EmailAddress)));
    }

    [Fact]
    public void InvalidEmailShowsLocalizedFormatMessage()
    {
        var model = new Link.EmailLinkModel { EmailAddress = "not-an-email" };

        var results = ValidateProperty(model, nameof(Link.EmailLinkModel.EmailAddress));

        Assert.Contains(results, r => r.ErrorMessage == SharedStrings.Validation_InvalidEmailFormat);
    }

    [Fact]
    public void EmptyGuaranteeEmailShowsLocalizedRequiredMessage()
    {
        var model = new Guarantee.GuaranteeModel { EmailAddress = "" };

        var results = ValidateProperty(model, nameof(Guarantee.GuaranteeModel.EmailAddress));

        Assert.Contains(results, r => r.ErrorMessage == SharedStrings.Validation_EmailRequired);
    }

    [Fact]
    public void PasswordMismatchShowsLocalizedMessage()
    {
        var model = new ResetPassword.PasswordResetModel
        {
            NewPassword = "correct horse",
            ConfirmPassword = "wrong horse",
        };

        var results = ValidateProperty(model, nameof(ResetPassword.PasswordResetModel.ConfirmPassword));

        Assert.Contains(results, r => r.ErrorMessage == SharedStrings.Validation_PasswordsDoNotMatch);
    }

    [Fact]
    public void ShortPasswordShowsLocalizedLengthMessage()
    {
        var model = new ResetPassword.PasswordResetModel { NewPassword = "short", ConfirmPassword = "short" };

        var results = ValidateProperty(model, nameof(ResetPassword.PasswordResetModel.NewPassword));

        // StringLength formats the resource with {2} = MinimumLength, so compare the rendered text.
        Assert.Contains(
            results,
            r => r.ErrorMessage is { } message && message.Contains('8') && !message.Contains('{'));
    }

    [Fact]
    public void EmptyPasswordShowsLocalizedRequiredMessage()
    {
        var model = new ResetPassword.PasswordResetModel { NewPassword = "", ConfirmPassword = "" };

        var results = ValidateProperty(model, nameof(ResetPassword.PasswordResetModel.NewPassword));

        Assert.Contains(results, r => r.ErrorMessage == SharedStrings.Validation_NewPasswordRequired);
    }

    [Fact]
    public void EmptyConfirmPasswordShowsLocalizedRequiredMessage()
    {
        var model = new ResetPassword.PasswordResetModel { NewPassword = "correct horse", ConfirmPassword = "" };

        var results = ValidateProperty(model, nameof(ResetPassword.PasswordResetModel.ConfirmPassword));

        Assert.Contains(results, r => r.ErrorMessage == SharedStrings.Validation_ConfirmPasswordRequired);
    }

    [Fact]
    public void EveryValidationMessageResolvesToText()
    {
        // A missing static property throws; a missing resx key silently returns "".
        Assert.False(string.IsNullOrWhiteSpace(SharedStrings.Validation_EmailRequired));
        Assert.False(string.IsNullOrWhiteSpace(SharedStrings.Validation_InvalidEmailFormat));
        Assert.False(string.IsNullOrWhiteSpace(SharedStrings.Validation_NewPasswordRequired));
        Assert.False(string.IsNullOrWhiteSpace(SharedStrings.Validation_PasswordLength));
        Assert.False(string.IsNullOrWhiteSpace(SharedStrings.Validation_ConfirmPasswordRequired));
        Assert.False(string.IsNullOrWhiteSpace(SharedStrings.Validation_PasswordsDoNotMatch));
    }

    private static List<ValidationResult> ValidateProperty(object model, string propertyName)
    {
        var results = new List<ValidationResult>();
        var context = new ValidationContext(model) { MemberName = propertyName };
        var value = model.GetType().GetProperty(propertyName)!.GetValue(model);

        Validator.TryValidateProperty(value, context, results);

        return results;
    }
}
