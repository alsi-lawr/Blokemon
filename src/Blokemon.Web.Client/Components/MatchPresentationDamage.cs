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
    internal static MatchPresentationOverlay Applied(
        MatchPresentationOverlay overlay,
        MatchEventCueView cue
    ) =>
        cue.Kind switch
        {
            MatchAnimationKindView.Damage => overlay.WithDamage(
                cue.TargetCardInstanceIds,
                cue.Amount
            ),
            MatchAnimationKindView.Heal => overlay.WithDamage(
                cue.TargetCardInstanceIds,
                -cue.Amount
            ),
            _ => overlay,
        };

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
