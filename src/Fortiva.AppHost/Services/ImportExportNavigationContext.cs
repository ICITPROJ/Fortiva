namespace Fortiva.AppHost.Services;

/// <summary>Navigation hint — expand duplicate management on Import / Export.</summary>
public sealed class ImportExportNavigationContext
{
    public bool FocusDuplicates { get; init; }

    public static ImportExportNavigationContext ShowDuplicates { get; } = new() { FocusDuplicates = true };

    private ImportExportNavigationContext() { }
}
