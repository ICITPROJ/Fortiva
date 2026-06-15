using Fortiva.Core.Vault;

namespace Fortiva.Core.Tests.Vault;

public sealed class VaultFilePrefetchTests
{
    [Fact]
    public void TryTake_ReturnsPrefetchedBytesOnce()
    {
        var path = CreateTempFile([1, 2, 3, 4]);
        try
        {
            var prefetch = new VaultFilePrefetch();
            prefetch.Begin(path);
            Thread.Sleep(200);

            var first = prefetch.TryTake(path);
            Assert.Equal([1, 2, 3, 4], first);
            Assert.Null(prefetch.TryTake(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryTake_ReturnsNullWhenFileChangedAfterPrefetch()
    {
        var path = CreateTempFile([9, 9, 9]);
        try
        {
            var prefetch = new VaultFilePrefetch();
            prefetch.Begin(path);
            Thread.Sleep(200);

            File.WriteAllBytes(path, [1, 1]);

            Assert.Null(prefetch.TryTake(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Begin_ReplacesStalePrefetchForNewPath()
    {
        var pathA = CreateTempFile([1]);
        var pathB = CreateTempFile([2]);
        try
        {
            var prefetch = new VaultFilePrefetch();
            prefetch.Begin(pathA);
            Thread.Sleep(150);
            prefetch.Begin(pathB);
            Thread.Sleep(150);

            Assert.Null(prefetch.TryTake(pathA));
            Assert.Equal([2], prefetch.TryTake(pathB));
        }
        finally
        {
            File.Delete(pathA);
            File.Delete(pathB);
        }
    }

    private static string CreateTempFile(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"fortiva-prefetch-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
