namespace FcDraft.Application.Common.Interfaces;

/// <summary>
/// The unbiased-shuffle seam for the server-authoritative spinner (PR-13, PRD §9.5). Mirrors the injected
/// <see cref="TimeProvider"/> pattern: production uses a Fisher–Yates shuffle over a cryptographically
/// unseeded RNG, while tests inject a deterministic permutation so the committed spinner order is
/// reproducible. The visual wheel only reveals whatever this produces, so it can never influence the result.
/// </summary>
public interface IShuffler
{
    /// <summary>Shuffles <paramref name="items"/> in place into an unbiased random order.</summary>
    void Shuffle<T>(IList<T> items);
}
