namespace Fortiva.AppHost.Services;

public sealed class HealthPageNavigationContext : IEquatable<HealthPageNavigationContext>
{
    public string? FocusIssue { get; init; }

    public static HealthPageNavigationContext ForIssue(string issue)
        => new() { FocusIssue = issue };

    public bool Equals(HealthPageNavigationContext? other)
        => other is not null
           && string.Equals(FocusIssue, other.FocusIssue, StringComparison.Ordinal);

    public override bool Equals(object? obj)
        => obj is HealthPageNavigationContext other && Equals(other);

    public override int GetHashCode()
        => FocusIssue?.GetHashCode(StringComparison.Ordinal) ?? 0;
}
