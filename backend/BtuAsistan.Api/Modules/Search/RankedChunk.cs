namespace BtuAsistan.Api.Modules.Search;

// One hit of a single search, in the search's own order (best first). Scores of different
// searches are on different scales and are not compared: fusion uses positions only
// (PLANNING S1, S4). The score is kept for logging and the search screen.
public sealed record RankedChunk(Guid ChunkId, double Score);
