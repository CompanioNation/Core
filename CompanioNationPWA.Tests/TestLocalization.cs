using System.Globalization;
using Microsoft.Extensions.Localization;

namespace CompanioNationPWA.Tests;

/// <summary>
/// Localizer that returns localization keys verbatim so tests can assert against
/// stable identifiers instead of language-specific resource strings.
/// </summary>
internal sealed class KeyReturningStringLocalizer : IStringLocalizer
{
    public LocalizedString this[string name] => new(name, name);

    public LocalizedString this[string name, params object[] arguments] =>
        new(name, string.Format(CultureInfo.InvariantCulture, name, arguments));

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
}

internal sealed class KeyReturningStringLocalizerFactory : IStringLocalizerFactory
{
    public IStringLocalizer Create(Type resourceSource) => new KeyReturningStringLocalizer();

    public IStringLocalizer Create(string baseName, string location) => new KeyReturningStringLocalizer();
}
