namespace Fortiva.AppHost.Services;

/// <summary>Navigation hint — focus a section on Import / Export.</summary>
public sealed class ImportExportNavigationContext : IEquatable<ImportExportNavigationContext>
{
    public bool FocusDuplicates { get; init; }

    public static ImportExportNavigationContext ShowDuplicates => new() { FocusDuplicates = true };

    public bool Equals(ImportExportNavigationContext? other)
        => other is not null && FocusDuplicates == other.FocusDuplicates;

    public override bool Equals(object? obj)
        => obj is ImportExportNavigationContext other && Equals(other);

    public override int GetHashCode() => FocusDuplicates.GetHashCode();
}
