using Microsoft.AspNetCore.Components.Forms;

namespace CompanioNationPWA.Tests.Fakes;

/// <summary>
/// Minimal in-memory <see cref="IBrowserFile"/> used to exercise upload handlers.
/// </summary>
public sealed class TestBrowserFile : IBrowserFile
{
    public string Name { get; set; } = "test.jpg";

    public DateTimeOffset LastModified { get; set; } = DateTimeOffset.UtcNow;

    public long Size { get; set; } = 1024;

    public string ContentType { get; set; } = "image/jpeg";

    public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default)
    {
        return new MemoryStream([1, 2, 3, 4]);
    }
}
