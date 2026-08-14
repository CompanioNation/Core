using System.Resources;

namespace CompanioNation.Shared;

/// <summary>
/// Strongly-typed accessor for localized DataAnnotation validation messages.
/// Resources live in SharedValidationStrings.resx and its culture variants.
/// </summary>
public sealed class SharedValidationStrings
{
    private static readonly ResourceManager ResourceManager =
        new("CompanioNation.Shared.SharedValidationStrings", typeof(SharedValidationStrings).Assembly);

    private SharedValidationStrings() { }

    public static string Validation_NameRequired => GetResource(nameof(Validation_NameRequired));
    public static string Validation_NameTooLong => GetResource(nameof(Validation_NameTooLong));
    public static string Validation_DescriptionRequired => GetResource(nameof(Validation_DescriptionRequired));
    public static string Validation_DescriptionTooLong => GetResource(nameof(Validation_DescriptionTooLong));
    public static string Validation_GenderRequired => GetResource(nameof(Validation_GenderRequired));
    public static string Validation_DateOfBirthRequired => GetResource(nameof(Validation_DateOfBirthRequired));
    public static string Validation_InvalidDate => GetResource(nameof(Validation_InvalidDate));
    public static string Validation_MinimumAge => GetResource(nameof(Validation_MinimumAge));
    public static string Validation_CityRequired => GetResource(nameof(Validation_CityRequired));
    public static string Validation_ProfilePictureRequired => GetResource(nameof(Validation_ProfilePictureRequired));

    private static string GetResource(string name) => ResourceManager.GetString(name) ?? string.Empty;
}
