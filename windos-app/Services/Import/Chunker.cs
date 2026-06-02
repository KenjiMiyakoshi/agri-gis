using System.Runtime.CompilerServices;

namespace AgriGis.Desktop.Services.Import;

// WB4 B404: IAsyncEnumerable<T> を指定サイズのチャンクに切る純粋ヘルパ。
public static class Chunker
{
    public static async IAsyncEnumerable<IReadOnlyList<T>> ChunkAsync<T>(
        IAsyncEnumerable<T> source,
        int size,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size));

        var buf = new List<T>(size);
        await foreach (var item in source.WithCancellation(ct))
        {
            buf.Add(item);
            if (buf.Count >= size)
            {
                yield return buf.ToArray();
                buf.Clear();
            }
        }
        if (buf.Count > 0) yield return buf.ToArray();
    }
}
