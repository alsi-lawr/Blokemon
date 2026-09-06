using Blokemon.App.Contracts;

namespace Blokemon.Web.Client.Components;

// What a step's cues so far have made true of the table it settles on, card by card.
//
// A command hands over one settled table, and each cue inside it is about some of the cards on
// it: the card that threw a blow paid for it, the card the blow landed on carries it, the card an
// Energy was attached to is holding it. Once a cue has played, the cards it named are drawn as
// the settled table has them, and every card no cue has named yet is drawn as the table before
// the command had it. So what a cue says has happened is on the table from the beat after it
// says so, rather than when the command's last cue has played - which for an attack is after the
// turn has changed hands and the opponent has drawn.
//
// A card the settled table no longer has anywhere - knocked out, evolved under, carried off - is
// never caught up: it goes on being drawn where it was until the cue that takes it away plays, and
// the concealment that cue starts is what keeps it off the table from then on. Catching it up
// would take it off the table before anything had been seen to happen to it.
internal sealed class MatchPresentationCaught(IEnumerable<MatchEventCueView> cues)
{
    private readonly HashSet<string> _cards = new(StringComparer.Ordinal);
    private readonly HashSet<string> _discarded = new(StringComparer.Ordinal);
    private readonly HashSet<string> _discarding = cues.Where(cue =>
            cue.Kind == MatchAnimationKindView.Discard
        )
        .Select(cue => cue.SourceCardInstanceId)
        .OfType<string>()
        .ToHashSet(StringComparer.Ordinal);

    // Whose turn it is, and the phase, change hands at the cue that says so.
    public bool Turned { get; private set; }

    // The prizes change at the cue that takes one.
    public bool Prized { get; private set; }

    // The cards this cue is about, once it has played.
    public void Played(MatchEventCueView cue)
    {
        switch (cue.Kind)
        {
            case MatchAnimationKindView.Discard:
                if (cue.SourceCardInstanceId is { } discarded)
                {
                    _discarded.Add(discarded);
                }
                break;
            case MatchAnimationKindView.Attack:
                Add(cue.SourceCardInstanceId);
                break;
            case MatchAnimationKindView.Damage:
            case MatchAnimationKindView.Heal:
            case MatchAnimationKindView.Condition:
                AddAll(cue.TargetCardInstanceIds);
                break;
            case MatchAnimationKindView.Play:
            case MatchAnimationKindView.Evolve:
            case MatchAnimationKindView.Attach:
                Add(cue.SourceCardInstanceId);
                AddAll(cue.TargetCardInstanceIds);
                break;
            case MatchAnimationKindView.Turn:
                Turned = true;
                break;
            case MatchAnimationKindView.Prize:
                Prized = true;
                break;
            default:
                break;
        }
    }

    // Whether this card is drawn as the settled table has it: named by a cue that has played, and
    // still somewhere on that table to be drawn from.
    public bool Standing(string cardInstanceId, MatchFrameView settled) =>
        _cards.Contains(cardInstanceId) && OnTheTable(settled, cardInstanceId);

    // The table between the two: the phase, the turn and the prizes as the cues so far have told
    // them, and each card from whichever of the two tables it has reached.
    public MatchFrameView Table(MatchFrameView before, MatchFrameView settled)
    {
        var table = Turned ? before with { Phase = settled.Phase, Round = settled.Round } : before;
        return table with
        {
            Player = Side(before.Player, settled.Player, settled),
            Opponent = Side(before.Opponent, settled.Opponent, settled),
        };
    }

    private MatchSideView Side(MatchSideView before, MatchSideView settled, MatchFrameView frame)
    {
        var shown = new HashSet<string>(StringComparer.Ordinal);
        // A card that has not caught up but whose place a caught-up card now stands in has been
        // moved on by the same cue - the Blokemon a taxi sent to the Booth - and is drawn where the
        // settled table has it rather than nowhere.
        var displaced = new HashSet<string>(StringComparer.Ordinal);

        var active = Placed(before.Active, settled.Active, frame, displaced, shown);
        var bench = Placed(before.Bench, settled.Bench, frame, displaced, shown);
        var kits = Placed(before.InPlayKits, settled.InPlayKits, frame, displaced, shown);
        return before with
        {
            HasTurn = Turned ? settled.HasTurn : before.HasTurn,
            PrizeCards = Prized ? settled.PrizeCards : before.PrizeCards,
            Active = active,
            Bench = bench,
            InPlayKits = kits,
        };
    }

    public MatchFrameView Discards(
        MatchFrameView shown,
        MatchFrameView before,
        MatchFrameView settled
    ) =>
        _discarding.Count == 0
            ? shown
            : shown with
            {
                Player = Discards(shown.Player, before.Player, settled.Player),
                Opponent = Discards(shown.Opponent, before.Opponent, settled.Opponent),
            };

    private MatchSideView Discards(
        MatchSideView shown,
        MatchSideView before,
        MatchSideView settled
    ) =>
        shown with
        {
            Active = shown.Active is null ? null : Attachments(shown.Active, before),
            Bench = [.. shown.Bench.Select(card => Attachments(card, before))],
            InPlayKits = [.. shown.InPlayKits.Select(card => Attachments(card, before))],
            EmptiesTray =
            [
                .. shown.EmptiesTray.Where(card =>
                    !_discarding.Contains(card.Id) || _discarded.Contains(card.Id)
                ),
                .. settled.EmptiesTray.Where(card =>
                    _discarded.Contains(card.Id)
                    && !shown.EmptiesTray.Any(existing => existing.Id == card.Id)
                ),
            ],
        };

    private MatchCardInstanceView Attachments(MatchCardInstanceView shown, MatchSideView before)
    {
        if (_discarding.Count == 0)
        {
            return shown;
        }

        var original = Found(before, shown.Id);
        return shown with
        {
            AttachedEnergy = Attachments(shown.AttachedEnergy, original?.AttachedEnergy ?? []),
            AttachedTools = Attachments(shown.AttachedTools, original?.AttachedTools ?? []),
        };
    }

    private MatchAttachedCardInstanceView[] Attachments(
        MatchAttachedCardInstanceView[] shown,
        MatchAttachedCardInstanceView[] before
    ) =>
        [
            // Catching up the attacker must not spend attachments whose discard cues have not played.
            .. before
                .Where(card =>
                    _discarding.Contains(card.Id) || shown.Any(current => current.Id == card.Id)
                )
                .Concat(shown)
                .DistinctBy(card => card.Id)
                .Where(card => !_discarded.Contains(card.Id)),
        ];

    private MatchCardInstanceView? Placed(
        MatchCardInstanceView? before,
        MatchCardInstanceView? settled,
        MatchFrameView frame,
        HashSet<string> displaced,
        HashSet<string> shown
    )
    {
        if (settled is not null && Standing(settled.Id, frame) && shown.Add(settled.Id))
        {
            if (before is not null && before.Id != settled.Id && !_cards.Contains(before.Id))
            {
                displaced.Add(before.Id);
            }

            return settled;
        }

        if (before is not null && !Standing(before.Id, frame) && shown.Add(before.Id))
        {
            return before;
        }

        return null;
    }

    // The cards of one place, position by position: a caught-up card at its settled position,
    // and otherwise the card that was there before the command, unless it has caught up and is
    // drawn elsewhere. Whatever that leaves undrawn is put after them, so nothing on either table
    // goes unseen while it should be seen.
    private MatchCardInstanceView[] Placed(
        MatchCardInstanceView[] before,
        MatchCardInstanceView[] settled,
        MatchFrameView frame,
        HashSet<string> displaced,
        HashSet<string> shown
    )
    {
        var placed = new List<MatchCardInstanceView>(Math.Max(before.Length, settled.Length));
        for (var index = 0; index < Math.Max(before.Length, settled.Length); index++)
        {
            var later = index < settled.Length ? settled[index] : null;
            var earlier = index < before.Length ? before[index] : null;
            if (
                later is not null
                && (Standing(later.Id, frame) || displaced.Contains(later.Id))
                && shown.Add(later.Id)
            )
            {
                placed.Add(later);
            }
            else if (
                earlier is not null
                && !Standing(earlier.Id, frame)
                && !displaced.Contains(earlier.Id)
                && shown.Add(earlier.Id)
            )
            {
                placed.Add(earlier);
            }
        }

        foreach (var card in settled)
        {
            if ((Standing(card.Id, frame) || displaced.Contains(card.Id)) && shown.Add(card.Id))
            {
                placed.Add(card);
            }
        }

        foreach (var card in before)
        {
            if (!Standing(card.Id, frame) && shown.Add(card.Id))
            {
                placed.Add(displaced.Contains(card.Id) ? Found(frame, card.Id) ?? card : card);
            }
        }

        return [.. placed];
    }

    private static bool OnTheTable(MatchFrameView frame, string cardInstanceId) =>
        Found(frame, cardInstanceId) is not null;

    private static MatchCardInstanceView? Found(MatchFrameView frame, string cardInstanceId) =>
        Found(frame.Player, cardInstanceId) ?? Found(frame.Opponent, cardInstanceId);

    private static MatchCardInstanceView? Found(MatchSideView side, string cardInstanceId)
    {
        if (side.Active?.Id == cardInstanceId)
        {
            return side.Active;
        }

        return side.Bench.FirstOrDefault(card => card.Id == cardInstanceId)
            ?? side.InPlayKits.FirstOrDefault(card => card.Id == cardInstanceId);
    }

    private void Add(string? cardInstanceId)
    {
        if (cardInstanceId is not null)
        {
            _cards.Add(cardInstanceId);
        }
    }

    private void AddAll(string[] cardInstanceIds)
    {
        foreach (var cardInstanceId in cardInstanceIds)
        {
            _cards.Add(cardInstanceId);
        }
    }
}
