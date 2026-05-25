namespace Fortiva.AppHost.Services;

/// <summary>Pre-filled fields when opening the full entry editor or quick-add flow.</summary>
public sealed class EntryDraft
{
    public string? Title { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
    public string? Url { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
}
