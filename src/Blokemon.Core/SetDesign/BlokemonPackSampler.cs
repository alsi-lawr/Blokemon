namespace Blokemon.Core.SetDesign;

public interface IBlokemonRandomSource
{
    int ConsumptionIndex { get; }

    int NextInt(int exclusiveMaximum);
}

public sealed class BlokemonSeededRandom(ulong seed) : IBlokemonRandomSource
{
    private ulong _state = seed;

    public int ConsumptionIndex { get; private set; }

    public int NextInt(int exclusiveMaximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(exclusiveMaximum);
        var bound = (ulong)exclusiveMaximum;
        var threshold = unchecked(0UL - bound) % bound;
        while (true)
        {
            var value = NextUInt64();
            if (value < threshold)
            {
                continue;
            }
            ConsumptionIndex++;
            return (int)(value % bound);
        }
    }

    private ulong NextUInt64()
    {
        _state += 0x9E3779B97F4A7C15UL;
        var value = _state;
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }
}

public static class BlokemonPackSampler
{
    public static string SampleSingle(
        BlokemonRuntimeManifest manifest,
        IBlokemonRandomSource random
    )
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(random);
        return manifest.Collectibles[random.NextInt(manifest.Collectibles.Length)].Id;
    }

    public static IReadOnlyList<string> SampleEleven(
        BlokemonRuntimeManifest manifest,
        IBlokemonRandomSource random
    )
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(random);
        var result = new List<string>(11);
        foreach (var slot in manifest.Products.Eleven.Slots)
        {
            var available = manifest
                .Collectibles.Where(card => card.ProductBucket == slot.Bucket)
                .OrderBy(static card => card.Id, StringComparer.Ordinal)
                .Select(static card => card.Id)
                .ToList();
            for (var count = 0; count < slot.Count; count++)
            {
                var index = random.NextInt(available.Count);
                result.Add(available[index]);
                available.RemoveAt(index);
            }
        }
        return result;
    }
}
