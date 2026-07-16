using FcDraft.Application.Common.Interfaces;

namespace FcDraft.Infrastructure.Drafts;

/// <summary>
/// The production <see cref="IShuffler"/> (PR-13): an unbiased Fisher–Yates shuffle over an injected
/// <see cref="Random"/> (<see cref="System.Random.Shared"/> in the composition root). Every permutation is
/// equally likely, so the committed spinner order is fair (PRD §9.5). Registered as a singleton alongside
/// <see cref="TimeProvider"/>; tests substitute a deterministic shuffler for reproducibility.
/// </summary>
public sealed class FisherYatesShuffler(Random random) : IShuffler
{
    public void Shuffle<T>(IList<T> items)
    {
        for (var index = items.Count - 1; index > 0; index--)
        {
            var swap = random.Next(index + 1);
            (items[index], items[swap]) = (items[swap], items[index]);
        }
    }
}
