using Blokemon.Core.SetDesign;

namespace Blokemon.Game;

public sealed record FrozenDeckSnapshot(PlayerId Owner, FrozenList<MechanicalCardId> Cards)
{
    public static FrozenDeckSnapshot Create(PlayerId owner, IEnumerable<string> mechanicalIds) =>
        new(
            owner,
            FrozenList<MechanicalCardId>.Create(
                mechanicalIds.Select(static id => new MechanicalCardId(id))
            )
        );
}

public sealed record MatchStartRequest(
    MatchId MatchId,
    MatchSeed Seed,
    FrozenDeckSnapshot FirstDeck,
    FrozenDeckSnapshot SecondDeck
);

public sealed record CardState(
    CardInstanceId Id,
    MechanicalCardId MechanicalId,
    PlayerId Owner,
    CardKind Kind,
    CardZone Zone,
    bool IsFaceDown,
    int StackPosition,
    CardInstanceId? AttachedTo,
    FrozenList<CardInstanceId> Attachments,
    FrozenList<CardInstanceId> UnderlyingCards,
    int Damage,
    FrozenList<RoughStateEntry> RoughStates,
    int EnteredAtOwnerRound,
    int LastPromotedRound
);

public sealed record PlayerState(
    PlayerId Id,
    int BarChitsRemaining,
    int MulliganCount,
    int MulliganBonusAllowance,
    bool MulliganBonusChosen,
    bool OpeningChosen,
    int RoundsStarted
);

public sealed record RoundUsage(
    PlayerId Player,
    int VimAttachments,
    int MatesPlayed,
    int LocalsPlayed,
    int TaxisUsed,
    FrozenList<EffectId> EffectsUsed
)
{
    public static RoundUsage Empty(PlayerId player) => new(player, 0, 0, 0, 0, []);
}

public sealed record MatchState(
    MatchId Id,
    string AuthorityVersion,
    MatchSeed Seed,
    MatchRandomState Random,
    MatchRevision Revision,
    long LastEventSequence,
    MatchPhase Phase,
    PlayerId OpeningPlayer,
    PlayerId ActivePlayer,
    int RoundNumber,
    FrozenList<PlayerState> Players,
    FrozenList<CardState> Cards,
    FrozenList<TemporaryEffect> Effects,
    FrozenList<CommandId> ProcessedCommands,
    RoundUsage RoundUsage,
    PendingEffectResolution? PendingEffect,
    PendingKnockoutResolution? PendingKnockout,
    FrozenList<PendingBarChitResolution> PendingBarChits,
    PlayerId? ReplacementPlayer,
    bool PendingRoundEnd,
    PlayerId? Winner,
    int SuddenDeathCount
)
{
    public PlayerState Player(PlayerId id) => Players.Single(player => player.Id == id);

    public CardState Card(CardInstanceId id) => Cards.Single(card => card.Id == id);

    public IEnumerable<CardState> CardsIn(PlayerId player, CardZone zone) =>
        Cards
            .Where(card => card.Owner == player && card.Zone == zone)
            .OrderBy(static card => card.StackPosition)
            .ThenBy(static card => card.Id);

    public CardState? Oche(PlayerId player) =>
        Cards.SingleOrDefault(card => card.Owner == player && card.Zone == CardZone.Oche);

    public PlayerId Other(PlayerId player) =>
        Players.Single(candidate => candidate.Id != player).Id;
}
