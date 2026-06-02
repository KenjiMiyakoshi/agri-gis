using AgriGis.Desktop.Services.Import;
using Xunit;

namespace AgriGis.Desktop.Tests.Tests.Services.Import;

// B504 (WB5): Chunker.ChunkAsync の境界テスト。
public sealed class ChunkerTests
{
    private static async IAsyncEnumerable<int> Range(int n)
    {
        for (int i = 0; i < n; i++)
        {
            await Task.Yield();
            yield return i;
        }
    }

    [Theory]
    [InlineData(0, 5, 0)]
    [InlineData(5, 5, 1)]
    [InlineData(10, 5, 2)]
    [InlineData(7, 3, 3)]   // 3 + 3 + 1
    public async Task ChunkAsync_Counts(int total, int size, int expectedChunks)
    {
        var chunks = new List<IReadOnlyList<int>>();
        await foreach (var c in Chunker.ChunkAsync(Range(total), size))
        {
            chunks.Add(c);
        }
        Assert.Equal(expectedChunks, chunks.Count);
        Assert.Equal(total, chunks.Sum(c => c.Count));
    }

    [Fact]
    public async Task ChunkAsync_PreservesOrder()
    {
        var seen = new List<int>();
        await foreach (var c in Chunker.ChunkAsync(Range(10), 3))
        {
            seen.AddRange(c);
        }
        Assert.Equal(Enumerable.Range(0, 10).ToArray(), seen.ToArray());
    }

    [Fact]
    public async Task ChunkAsync_InvalidSize_Throws()
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
        {
            await foreach (var _ in Chunker.ChunkAsync(Range(5), 0)) { }
        });
    }
}
