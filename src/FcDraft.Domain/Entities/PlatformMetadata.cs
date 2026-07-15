namespace FcDraft.Domain.Entities;

/// <summary>
/// A single key/value platform configuration record. Seeded and read from the database so the
/// application has durable metadata (schema marker, platform name) alongside the user directory.
/// </summary>
public sealed class PlatformMetadata
{
    public required string Key { get; set; }
    public required string Value { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
