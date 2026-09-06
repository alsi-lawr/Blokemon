using Blokemon.App.Contracts;
using Blokemon.Web.Client.Components;

namespace Blokemon.Web.Client.Pages;

// ---- The aura model -------------------------------------------------------------------
//
// Every card that can be acted on glows. Which cards those are depends only on the stage:
// the playable cards while idle, the forced decision's own candidates while a decision is
// outstanding, the picked-up card and its targets once one is picked up, and the eligible
// cards while a choice step is open. It is derived here, every render, and handed to the
// presenters.
//
// Picking a card up focuses the table on what it can do: the card holds the chosen glow, the
// places it can go glow as targets, and nothing else is lit. Every move has a target. Where the
// engine names one it is that card; where it names none the table supplies the place the
// physical game would use, and a move with no such place is used where the card stands.
public partial class Match
{
    private static readonly MatchActionKindView[] _forcedKinds =
    [
        MatchActionKindView.ChooseMulliganBonus,
        MatchActionKindView.ChooseOpening,
        MatchActionKindView.ChooseBonusPlacement,
        MatchActionKindView.ChooseReplacement,
        MatchActionKindView.ResolveKnockout,
        MatchActionKindView.TakePrize,
        MatchActionKindView.ResolveChoice,
    ];

    private static readonly MatchActionKindView[] _cardKinds =
    [
        MatchActionKindView.AttachEnergy,
        MatchActionKindView.PlayBlokemon,
        MatchActionKindView.Evolve,
        MatchActionKindView.PlayTrainer,
        MatchActionKindView.UsePokemonPower,
        MatchActionKindView.Attack,
        MatchActionKindView.Retreat,
        MatchActionKindView.DiscardFossil,
    ];

    private static readonly IReadOnlyDictionary<string, int> _noCounters = new Dictionary<
        string,
        int
    >(StringComparer.Ordinal);

    // Where a move goes: a card the table shows, or a fixed place on it.
    private readonly record struct MatchTarget(string? CardInstanceId, MatchTargetPlaces Place)
    {
        public static MatchTarget Card(string cardInstanceId) => new(cardInstanceId, default);

        public static MatchTarget At(MatchTargetPlaces place) => new(null, place);
    }

    private MatchAuraView Auras(MatchActionView[] forced)
    {
        if (_view?.Match is not { } match || Busy())
        {
            return MatchAuraView.Rest(_noCounters);
        }

        var pickable = Pickable(match);
        switch (_stage)
        {
            // A draw is asked for by the Deck itself: it glows, and tapping it takes the cards.
            case Stage.Idle when forced.Length > 0 && ForcedByDeck(forced):
                return MatchAuraView.Rest(_noCounters) with { Deck = true };

            case Stage.Idle when forced.Length > 0:
                return ForcedByAura(forced)
                    ? MatchAuraView.Rest(_noCounters) with
                    {
                        Cards = pickable,
                        Draggable = pickable,
                    }
                    : MatchAuraView.Rest(_noCounters);

            // A card that has been picked up holds the chosen glow and nothing else offers
            // itself: the table is focused on what this card can do. With no place on the table
            // for its one move, putting it down is a tap away on the card itself.
            case Stage.Armed:
                return MatchAuraView.Rest(_noCounters) with
                {
                    Selected = [_originCardInstanceId!],
                    Draggable = pickable,
                    Focused = true,
                };

            case Stage.Destination:
                return MatchAuraView.Rest(_noCounters) with
                {
                    Selected = [_originCardInstanceId!],
                    Targets = TargetCardIds(),
                    Draggable = pickable,
                    Places = TargetPlaces(),
                    Focused = true,
                };

            case Stage.Actions:
            case Stage.Confirm:
                return MatchAuraView.Rest(_noCounters) with
                {
                    Selected =
                    [
                        .. new[]
                        {
                            _originCardInstanceId,
                            _destinationCardInstanceId,
                        }.OfType<string>(),
                    ],
                    Focused = _originCardInstanceId is not null,
                };

            case Stage.Choice when CurrentRequirement() is { } requirement:
                return ChoiceAuras(requirement);

            case Stage.Idle:
                return MatchAuraView.Rest(_noCounters) with
                {
                    Cards = pickable,
                    Draggable = pickable,
                };

            default:
                return MatchAuraView.Rest(_noCounters);
        }
    }

    private MatchAuraView ChoiceAuras(MatchChoiceRequirementView requirement)
    {
        var draft = Draft(requirement);
        var origin = _originCardInstanceId is null ? [] : new[] { _originCardInstanceId };
        var focused = _originCardInstanceId is not null;
        return requirement.Kind switch
        {
            MatchChoiceKindView.Cards => MatchAuraView.Rest(_noCounters) with
            {
                Cards = [.. requirement.EligibleCards.Select(static card => card.Id)],
                Selected = [.. draft.Cards],
                Focused = focused,
            },
            MatchChoiceKindView.Distribution => MatchAuraView.Rest(draft.Distribution) with
            {
                Cards = [.. requirement.EligibleCards.Select(static card => card.Id)],
                Selected =
                [
                    .. draft
                        .Distribution.Where(static item => item.Value > 0)
                        .Select(static item => item.Key),
                ],
                Focused = focused,
            },
            MatchChoiceKindView.Attachments when _attachmentCardInstanceId is { } energy =>
                MatchAuraView.Rest(_noCounters) with
                {
                    Cards = [.. requirement.EligibleTargets.Select(static card => card.Id)],
                    Selected = [energy],
                    Focused = focused,
                },
            MatchChoiceKindView.Attachments => MatchAuraView.Rest(_noCounters) with
            {
                Cards =
                [
                    .. requirement
                        .EligibleCards.Select(static card => card.Id)
                        .Where(card => !draft.Attachments.ContainsKey(card)),
                ],
                Selected = [.. draft.Attachments.Keys],
                Focused = focused,
            },
            _ => MatchAuraView.Rest(_noCounters) with { Selected = origin, Focused = focused },
        };
    }

    // The cards a tap or a drag picks up: the candidates of a decision the match posed while one
    // is outstanding, and every card with a move of its own otherwise. The same set whatever the
    // stage, because picking up another card while one is held puts the first one down.
    private string[] Pickable(MatchView match)
    {
        var posed = PosedByAura(match);
        return posed.Length > 0
            ? [.. posed.Select(static option => option.SourceCardInstanceId!).Distinct()]
            : PlayableCardIds(match);
    }

    // The candidates of a decision the match posed that the table can answer by its cards, found
    // whatever the stage: a candidate picked up and put down again is still a candidate.
    private MatchActionView[] PosedByAura(MatchView match)
    {
        if (_animating || _working)
        {
            return [];
        }

        foreach (var kind in _forcedKinds)
        {
            var options = match.LegalActions.Where(action => action.Kind == kind).ToArray();
            if (options.Length > 0)
            {
                return ForcedByAura(options) ? options : [];
            }
        }

        return [];
    }

    private string[] PlayableCardIds(MatchView match) =>
        [
            .. match
                .LegalActions.Where(action =>
                    IsCardAction(action) && IsVisible(action.SourceCardInstanceId)
                )
                .Select(static action => action.SourceCardInstanceId!)
                .Distinct(StringComparer.Ordinal),
        ];

    private string[] TargetCardIds() =>
        [
            .. OriginActions()
                .SelectMany(TargetsOf)
                .Select(static target => target.CardInstanceId)
                .OfType<string>()
                .Distinct(StringComparer.Ordinal),
        ];

    private MatchTargetPlaces TargetPlaces() =>
        OriginActions()
            .SelectMany(TargetsOf)
            .Aggregate(MatchTargetPlaces.None, static (places, target) => places | target.Place);

    // Every move the picked-up card offers: its own moves, or, while the match is waiting on a
    // decision, its candidacy for that decision.
    private MatchActionView[] OriginActions() =>
        _view?.Match is { } match && _originCardInstanceId is { } origin
            ?
            [
                .. match.LegalActions.Where(action =>
                    action.SourceCardInstanceId == origin
                    && action.DisabledReason is null
                    && (IsCardAction(action) || _forcedKinds.Contains(action.Kind))
                ),
            ]
            : [];

    // Where a move goes. The engine names the card where the move has one; where it names none,
    // the table supplies the place the physical game would use: a Blokemon comes down on an empty
    // Bench position, a Kit stands in the In-play place, a Fossil is chucked into the Empties
    // Tray, an attack is thrown at the opponent's Active, a retreat brings the Bench Blokemon to
    // the Active, and the chosen Active goes into the empty Active position. A Power whose first
    // question is a card on the table is aimed at that card. A move with none of those is used
    // where the card stands, and answers to a tap there.
    private IEnumerable<MatchTarget> TargetsOf(MatchActionView action)
    {
        if (action.TargetCardInstanceId is { } named)
        {
            if (IsVisible(named))
            {
                yield return MatchTarget.Card(named);
            }

            yield break;
        }

        var frame = DisplayFrame();
        switch (action.Kind)
        {
            case MatchActionKindView.PlayBlokemon:
            case MatchActionKindView.ChooseBonusPlacement:
                if (frame.Player.Bench.Length < 5)
                {
                    yield return MatchTarget.At(MatchTargetPlaces.Bench);
                }
                break;

            case MatchActionKindView.PlayTrainer:
                yield return MatchTarget.At(MatchTargetPlaces.InPlay);
                break;

            case MatchActionKindView.DiscardFossil:
                yield return MatchTarget.At(MatchTargetPlaces.Empties);
                break;

            case MatchActionKindView.Attack:
                if (frame.Opponent.Active is { } defender)
                {
                    yield return MatchTarget.Card(defender.Id);
                }
                break;

            case MatchActionKindView.Retreat:
                if (frame.Player.Active is { } active)
                {
                    yield return MatchTarget.Card(active.Id);
                }
                break;

            case MatchActionKindView.ChooseOpening:
            case MatchActionKindView.ChooseReplacement:
                if (frame.Player.Active is null)
                {
                    yield return MatchTarget.At(MatchTargetPlaces.Active);
                }
                break;

            case MatchActionKindView.UsePokemonPower:
                if (TableQuestion(action) is { } question)
                {
                    foreach (var card in question.EligibleCards)
                    {
                        if (IsVisible(card.Id))
                        {
                            yield return MatchTarget.Card(card.Id);
                        }
                    }
                }
                break;

            default:
                break;
        }
    }

    // The first thing a move asks, when it is a card the table shows: the answer can be given by
    // putting the move's card on it.
    private MatchChoiceRequirementView? TableQuestion(MatchActionView action) =>
        LocalRequirements(action).FirstOrDefault() is { Kind: MatchChoiceKindView.Cards } first
        && first.EligibleCards.Any(card => IsVisible(card.Id))
            ? first
            : null;

    // The moves of the picked-up card that have nowhere on the table to go: they are used where
    // the card stands, and a sheet has to list them.
    private MatchActionView[] InPlaceActions() =>
        [.. OriginActions().Where(action => !TargetsOf(action).Any())];

    // A destination step disambiguates itself when every move the chosen card offers goes to a
    // place the table shows: the places glow, and tapping one says everything a sheet could have
    // asked. A move with no place on the table to stand for it keeps its sheet.
    private bool DestinationIsUnambiguous() =>
        OriginActions().Length > 0 && InPlaceActions().Length == 0;

    // A move the engine offers without the means to pay for it is shown rather than played: it
    // never glows, is never an origin, and is never what a tap settles on. The dock says what it
    // needs instead.
    private static bool IsCardAction(MatchActionView action) =>
        action.SourceCardInstanceId is not null
        && action.DisabledReason is null
        && _cardKinds.Contains(action.Kind);

    private static MatchActionView[] UnavailableCardActions(
        string cardInstanceId,
        MatchView match
    ) =>
        [
            .. match.LegalActions.Where(action =>
                action.DisabledReason is not null && action.SourceCardInstanceId == cardInstanceId
            ),
        ];

    private bool IsVisible(string? cardInstanceId) =>
        cardInstanceId is not null
        && AllVisibleCards(DisplayFrame()).Any(card => card.Id == cardInstanceId);

    // A forced decision is outstanding whenever the engine offers one of its kinds and no
    // player-driven flow is in progress. It is derived, never stored, so the auras, the sheet
    // and the tap routing of one render always agree.
    private MatchActionView[] ForcedDecision(MatchView match)
    {
        if (_stage != Stage.Idle || _animating || _working)
        {
            return [];
        }

        foreach (var kind in _forcedKinds)
        {
            var options = match.LegalActions.Where(action => action.Kind == kind).ToArray();
            if (options.Length > 0)
            {
                return options;
            }
        }

        return [];
    }

    // A decision whose every candidate is a card the table shows hands off to the auras.
    private bool ForcedByAura(MatchActionView[] forced) =>
        forced.All(option => IsVisible(option.SourceCardInstanceId));

    // A draw is taken from the Deck, so the Deck is what glows and what is tapped.
    private static bool ForcedByDeck(MatchActionView[] forced) =>
        forced[0].Kind == MatchActionKindView.ChooseMulliganBonus;

    // One card a tap, the way a card is taken off a real deck. The engine offers one action per
    // count it would accept, from taking none upwards, under a stable key that sorts by that
    // count: the one after declining is the single card. What is left of the allowance is still
    // offered afterwards, so the Deck keeps glowing until it has all been taken.
    private static MatchActionView DeckDraw(MatchActionView[] forced) =>
        forced.Length > 1 ? forced[1] : forced[0];

    // A decision with one possible answer is not a decision. It is taken as soon as it is
    // offered, so nothing goes on screen to ask a question that has already answered itself.
    private bool ForcedIsAutomatic(MatchActionView[] forced) =>
        forced.Length == 1 && !ForcedByAura(forced) && !ForcedByDeck(forced);

    private static MatchActionView? GlobalAction(MatchView match, MatchActionKindView kind) =>
        match.LegalActions.FirstOrDefault(action => action.Kind == kind);

    private MatchActionView[] CardActions(string cardInstanceId, MatchView match) =>
        [
            .. match.LegalActions.Where(action =>
                IsCardAction(action) && action.SourceCardInstanceId == cardInstanceId
            ),
        ];
}
