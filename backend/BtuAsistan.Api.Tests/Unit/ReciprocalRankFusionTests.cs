using BtuAsistan.Api.Modules.Search;

namespace BtuAsistan.Api.Tests.Unit;

// PLANNING S4: only positions count; k = 5 is the setting chosen in Faz 3.
public sealed class ReciprocalRankFusionTests
{
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid C = Guid.Parse("00000000-0000-0000-0000-00000000000c");
    private static readonly Guid D = Guid.Parse("00000000-0000-0000-0000-00000000000d");

    private static List<RankedChunk> Ranking(params Guid[] ids) =>
        ids.Select((id, i) => new RankedChunk(id, 100 - i)).ToList();

    [Fact]
    public void Chunk_high_in_both_rankings_beats_top_of_one()
    {
        // B is second in both; A first only in the keyword search, D first only in vectors.
        var fused = new ReciprocalRankFusion(5).Fuse([Ranking(A, B, C), Ranking(D, B)]);

        Assert.Equal(B, fused[0].ChunkId);
    }

    [Fact]
    public void Score_is_the_sum_of_one_over_k_plus_position()
    {
        var fused = new ReciprocalRankFusion(5).Fuse([Ranking(A, B), Ranking(B)]);

        Assert.Equal((1.0 / 7) + (1.0 / 6), fused.Single(c => c.ChunkId == B).Score, precision: 10);
        Assert.Equal(1.0 / 6, fused.Single(c => c.ChunkId == A).Score, precision: 10);
    }

    [Fact]
    public void Chunk_found_by_both_appears_once()
    {
        var fused = new ReciprocalRankFusion(5).Fuse([Ranking(A, B), Ranking(B, A)]);

        Assert.Equal(2, fused.Count);
    }

    // Same scores: the order must not change between runs of the same question.
    [Fact]
    public void Ties_are_broken_by_id()
    {
        var fused = new ReciprocalRankFusion(5).Fuse([Ranking(C, A), Ranking(A, C)]);

        Assert.Equal([A, C], fused.Select(c => c.ChunkId));
    }

    [Fact]
    public void One_ranking_keeps_its_order()
    {
        var fused = new ReciprocalRankFusion(60).Fuse([Ranking(C, A, B)]);

        Assert.Equal([C, A, B], fused.Select(c => c.ChunkId));
    }

    [Fact]
    public void No_rankings_give_nothing()
    {
        Assert.Empty(new ReciprocalRankFusion(5).Fuse([Ranking(), Ranking()]));
    }
}
