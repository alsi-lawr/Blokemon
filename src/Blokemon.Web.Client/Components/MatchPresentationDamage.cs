using Blokemon.App.Contracts;

namespace Blokemon.Web.Client.Components;

// What a blow has done and who it was aimed at, worked out ahead of it being drawn.
//
// Damage lands while the cue announcing it is on screen rather than at the frame change that
// follows the last cue, so the amount has to be carried as a delta - and a blow has to know where
// it is going before it is thrown, which is knowable here only because the whole step is laid out
// before any of it is played.
internal static class MatchPresentationDamage
{
    // The counters the table shows over what the frame behind each card carries, for the beat at
    // which the cue at `played` is on screen.
    //
    // A card is drawn either from the table before the command or from the one it settles on,
    // and the two need opposite corrections. The table before the command has none of the
    // command's counters, so a card drawn from it carries the damage of every cue that has played
    // so far. The settled table has every counter the command places, including those whose cue
    // is still to come, so a card drawn from it has those held back until each one plays. Either
    // way a counter lands with the cue announcing it, whichever table the card is drawn from.
    internal static IReadOnlyDictionary<string, int> Deltas(
        MatchEventCueView[] cues,
        int played,
        Func<string, bool> settled
    )
    {
        var deltas = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < cues.Length; index++)
        {
            var amount = cues[index].Kind switch
            {
                MatchAnimationKindView.Damage => cues[index].Amount,
                MatchAnimationKindView.Heal => -cues[index].Amount,
                _ => 0,
            };
            if (amount == 0)
            {
                continue;
            }

            foreach (var target in cues[index].TargetCardInstanceIds)
            {
                var share = settled(target)
                    ? (index > played ? -amount : 0)
                    : (index <= played ? amount : 0);
                if (share != 0)
                {
                    deltas[target] = deltas.GetValueOrDefault(target) + share;
                }
            }
        }

        return deltas;
    }

    // What a declared blow goes on to damage, which is what it should be aimed at: an attack that
    // reaches past the card standing opposite and hits the Bench is aimed at the Bench. Every
    // card the same swing damages is collected, so a blow that catches several is aimed between
    // them rather than at whichever of them the engine happened to name first.
    //
    // A card that damages itself is left out. It is throwing the blow, and a card cannot both
    // throw one and be knocked back by it: the movement it is already making is the cause of what
    // is happening to it. An attack whose only damage is to itself - a fumble - is therefore
    // aimed at nothing, and turns where it stands rather than crossing the table at nobody.
    internal static IReadOnlyList<string> Struck(
        MatchEventCueView[] cues,
        string? strikingCardInstanceId
    )
    {
        var struck = new List<string>(2);
        foreach (var cue in cues)
        {
            if (
                cue.Kind != MatchAnimationKindView.Damage
                || cue.SourceCardInstanceId != strikingCardInstanceId
            )
            {
                continue;
            }

            foreach (var target in cue.TargetCardInstanceIds)
            {
                if (
                    target != strikingCardInstanceId
                    && !struck.Contains(target, StringComparer.Ordinal)
                )
                {
                    struck.Add(target);
                }
            }
        }

        return struck;
    }
}
