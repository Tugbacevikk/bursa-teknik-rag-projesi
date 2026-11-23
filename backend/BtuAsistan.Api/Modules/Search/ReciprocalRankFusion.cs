namespace BtuAsistan.Api.Modules.Search;

// Merges the rankings of several searches into one (PLANNING S4).
public interface IRankFusion
{
    // Best first. A chunk found by several searches appears once.
    IReadOnlyList<RankedChunk> Fuse(IReadOnlyList<IReadOnlyList<RankedChunk>> rankings);
}

// Reciprocal Rank Fusion. Keyword scores (around 0.01) and cosine similarities (around 0.6)
// are on different scales and cannot be added, so only positions count: a chunk at position
// p (1-based) of a ranking earns 1 / (k + p), and its earnings from all rankings are summed.
// A chunk near the top of both lists beats one at the very top of only one. k damps the
// difference between the first few positions; 60 is the value from the original paper.
public sealed class ReciprocalRankFusion(int k) : IRankFusion
{
    public IReadOnlyList<RankedChunk> Fuse(IReadOnlyList<IReadOnlyList<RankedChunk>> rankings)
    {
        var scores = new Dictionary<Guid, double>();

        foreach (var ranking in rankings)
        {
            for (var position = 1; position <= ranking.Count; position++)
            {
                var chunkId = ranking[position - 1].ChunkId;
                scores[chunkId] = scores.GetValueOrDefault(chunkId) + 1.0 / (k + position);
            }
        }

        // Ties (e.g. first in one list, second in the other) are broken by id, so the same
        // question always gives the same order.
        return scores
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key)
            .Select(pair => new RankedChunk(pair.Key, pair.Value))
            .ToList();
    }
}
