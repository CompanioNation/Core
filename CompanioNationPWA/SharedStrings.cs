using System.Resources;

namespace CompanioNationPWA;

/// <summary>
/// Marker type for shared UI strings used across components (IStringLocalizer&lt;SharedStrings&gt;),
/// and strongly-typed accessor for the DataAnnotation validation messages. DataAnnotations resolves
/// ErrorMessageResourceType by reflection for a STATIC PROPERTY, so those messages cannot go through
/// IStringLocalizer and must be exposed here as static properties.
/// Resources live at Resources/SharedStrings.resx and its culture variants.
/// </summary>
public sealed class SharedStrings
{
    private static readonly ResourceManager ResourceManager =
        new("CompanioNationPWA.Resources.SharedStrings", typeof(SharedStrings).Assembly);

    private SharedStrings() { }

    public static string Validation_EmailRequired => GetResource(nameof(Validation_EmailRequired));
    public static string Validation_InvalidEmailFormat => GetResource(nameof(Validation_InvalidEmailFormat));
    public static string Validation_NewPasswordRequired => GetResource(nameof(Validation_NewPasswordRequired));
    public static string Validation_PasswordLength => GetResource(nameof(Validation_PasswordLength));
    public static string Validation_ConfirmPasswordRequired => GetResource(nameof(Validation_ConfirmPasswordRequired));
    public static string Validation_PasswordsDoNotMatch => GetResource(nameof(Validation_PasswordsDoNotMatch));

    private static string GetResource(string name) => ResourceManager.GetString(name) ?? string.Empty;
}
