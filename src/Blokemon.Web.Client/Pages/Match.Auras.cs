using Blokemon.App.Contracts;
using Blokemon.Web.Client.Components;

namespace Blokemon.Web.Client.Pages;

// ---- The aura model -------------------------------------------------------------------
//
// Every card that can be acted on glows. Which cards those are depends only on the stage:
// the playable cards while idle, the forced decision's own candidates while a decision is
// outstanding, the destinations once an origin is picked, and the eligible cards while a
// choice step is open. It is derived here, every render, and handed to the presenters.
public partial class Match
{
    private static readonly MatchActionKindView[] _forcedKinds =
    [
        MatchActionKindView.ChooseMulliganBonus,
        MatchActionKindView.ChooseOpening,
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
        MatchActionKindView.UseAbility,
        MatchActionKindView.Attack,
        MatchActionKindView.Retreat,
        MatchActionKindView.DiscardFossil,
    ];

    private static readonly IReadOnlyDictionary<string, int> _noCounters = new Dictionary<
        string,
        int
    >(StringComparer.Ordinal);

    private MatchAuraView Auras(MatchActionView[] forced)
    {
        if (_view?.Match is not { } match || Busy())
        {
            return new([], [], false, false, _noCounters);
        }

        switch (_stage)
        {
            // A draw is asked for by the Deck itself: it glows, and tapping it takes the cards.
            case Stage.Idle when forced.Length > 0 && ForcedByDeck(forced):
                return new([], [], false, true, _noCounters);

            case Stage.Idle when forced.Length > 0:
                return ForcedByAura(forced)
                    ? new(
                        [.. forced.Select(static option => option.SourceCardInstanceId!)],
                        [],
                        false,
                        false,
                        _noCounters
                    )
                    : new([], [], false, false, _noCounters);

            // A card that has been picked up holds the chosen glow while everything else that
            // could be played still offers itself: putting this one down is a tap away either
            // way, on the card itself to play it or on another to swap to that one.
            case Stage.Armed:
                return new(
                    PlayableCardIds(match),
                    [_originCardInstanceId!],
                    false,
                    false,
                    _noCounters
                );

            case Stage.Destination:
                return new(
                    DestinationCardIds(),
                    [_originCardInstanceId!],
                    _benchDestination,
                    false,
                    _noCounters
                );

            case Stage.Actions:
            case Stage.Confirm:
                return new(
                    [],
                    [
                        .. new[]
                        {
                            _originCardInstanceId,
                            _destinationCardInstanceId,
                        }.OfType<string>(),
                    ],
                    false,
                    false,
                    _noCounters
                );

            case Stage.Choice when CurrentRequirement() is { } requirement:
                return ChoiceAuras(requirement);

            case Stage.Idle:
                return new(PlayableCardIds(match), [], false, false, _noCounters);

            default:
                return new([], [], false, false, _noCounters);
        }
    }

    private MatchAuraView ChoiceAuras(MatchChoiceRequirementView requirement)
    {
        var draft = Draft(requirement);
        var origin = _originCardInstanceId is null ? [] : new[] { _originCardInstanceId };
        return requirement.Kind switch
        {
            MatchChoiceKindView.Cards => new(
                [.. requirement.EligibleCards.Select(static card => card.Id)],
                [.. draft.Cards],
                false,
                false,
                _noCounters
            ),
            MatchChoiceKindView.Distribution => new(
                [.. requirement.EligibleCards.Select(static card => card.Id)],
                [
                    .. draft
                        .Distribution.Where(static item => item.Value > 0)
                        .Select(static item => item.Key),
                ],
                false,
                false,
                draft.Distribution
            ),
            MatchChoiceKindView.Attachments when _attachmentCardInstanceId is { } energy => new(
                [.. requirement.EligibleTargets.Select(static card => card.Id)],
                [energy],
                false,
                false,
                _noCounters
            ),
            MatchChoiceKindView.Attachments => new(
                [
                    .. requirement
                        .EligibleCards.Select(static card => card.Id)
                        .Where(card => !draft.Attachments.ContainsKey(card)),
                ],
                [.. draft.Attachments.Keys],
                false,
                false,
                _noCounters
            ),
            _ => new([], origin, false, false, _noCounters),
        };
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

    private string[] DestinationCardIds() =>
        [
            .. OriginActions()
                .Select(static action => action.TargetCardInstanceId)
                .OfType<string>()
                .Distinct(StringComparer.Ordinal),
        ];

    private MatchActionView[] OriginActions() =>
        _view?.Match is { } match && _originCardInstanceId is { } origin
            ?
            [
                .. match.LegalActions.Where(action =>
                    IsCardAction(action) && action.SourceCardInstanceId == origin
                ),
            ]
            : [];

    // A destination step disambiguates itself when everything the chosen card can do is the same
    // one kind of move onto a place the table shows: the places glow, and tapping one says
    // everything a sheet could have asked. A second kind of move, or a move with no place on the
    // table to stand for it, is a real choice between effects and keeps its sheet.
    private bool DestinationIsUnambiguous()
    {
        if (_directActions.Length > 0)
        {
            return false;
        }

        var actions = OriginActions();
        return actions.Length > 0
            && actions.DistinctBy(static action => action.Kind).Count() == 1
            && actions.All(IsBoardPosition);
    }

    // A place on the table is either a card that is on it or, for a Blokemon coming down, an
    // empty Bench position - and the Bench is only a place while one is free.
    private bool IsBoardPosition(MatchActionView action) =>
        action.TargetCardInstanceId is { } target
            ? IsVisible(target)
            : action.Kind == MatchActionKindView.PlayBlokemon && _benchDestination;

    private static bool IsCardAction(MatchActionView action) =>
        action.SourceCardInstanceId is not null && _cardKinds.Contains(action.Kind);

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
