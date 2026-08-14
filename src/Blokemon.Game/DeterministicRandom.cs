namespace Blokemon.Game;

internal sealed class DeterministicRandom(MatchRandomState state)
{
    private ulong _state = state.State;

    public int ConsumptionIndex { get; private set; } = state.ConsumptionIndex;

    public MatchRandomState Snapshot => new(_state, ConsumptionIndex);

    public int NextInt(int exclusiveMaximum)
    {
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
