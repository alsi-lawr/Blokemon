using Blokemon.Core.SetDesign;

namespace Blokemon.Game;

internal sealed class MatchBuilder
{
    private readonly AuthorityCatalog _catalog;
    private readonly List<PlayerState> _players;
    private readonly List<CardState> _cards;
    private readonly List<TemporaryEffect> _effects;
    private readonly List<CommandId> _processedCommands;
    private readonly List<PendingBarChitResolution> _pendingBarChits;

    public MatchBuilder(MatchState state, AuthorityCatalog catalog)
    {
        _catalog = catalog;
        Id = state.Id;
        AuthorityVersion = state.AuthorityVersion;
        Seed = state.Seed;
        Random = new DeterministicRandom(state.Random);
        Revision = state.Revision;
        LastEventSequence = state.LastEventSequence;
        Phase = state.Phase;
        OpeningPlayer = state.OpeningPlayer;
        ActivePlayer = state.ActivePlayer;
        RoundNumber = state.RoundNumber;
        _players = [.. state.Players];
        _cards = [.. state.Cards];
        _effects = [.. state.Effects];
        _processedCommands = [.. state.ProcessedCommands];
        RoundUsage = state.RoundUsage;
        PendingEffect = state.PendingEffect;
        PendingKnockout = state.PendingKnockout;
        _pendingBarChits = [.. state.PendingBarChits];
        ReplacementPlayer = state.ReplacementPlayer;
        PendingRoundEnd = state.PendingRoundEnd;
        Winner = state.Winner;
        SuddenDeathCount = state.SuddenDeathCount;
    }

    public MatchId Id { get; }

    public string AuthorityVersion { get; }

    public MatchSeed Seed { get; }

    public DeterministicRandom Random { get; }

    public MatchRevision Revision { get; set; }

    public long LastEventSequence { get; set; }

    public MatchPhase Phase { get; set; }

    public PlayerId OpeningPlayer { get; }

    public PlayerId ActivePlayer { get; set; }

    public int RoundNumber { get; set; }

    public RoundUsage RoundUsage { get; set; }

    public PendingEffectResolution? PendingEffect { get; set; }

    public PendingKnockoutResolution? PendingKnockout { get; set; }

    public IEnumerable<PendingBarChitResolution> PendingBarChits => _pendingBarChits;

    public void QueueBarChit(PendingBarChitResolution pending) => _pendingBarChits.Add(pending);

    public void RemoveBarChit(PendingBarChitResolution pending) => _pendingBarChits.Remove(pending);

    public PlayerId? ReplacementPlayer { get; set; }

    public bool PendingRoundEnd { get; set; }

    public PlayerId? Winner { get; set; }

    public int SuddenDeathCount { get; set; }

    public List<PendingMatchEvent> Events { get; } = [];

    public IEnumerable<PlayerState> Players => _players;

    public IEnumerable<CardState> Cards => _cards;

    public IEnumerable<TemporaryEffect> Effects => _effects;

    public IEnumerable<CommandId> ProcessedCommands => _processedCommands;

    public void RecordCommand(CommandId command) => _processedCommands.Add(command);

    public PlayerState Player(PlayerId id) => _players.Single(player => player.Id == id);

    public void SetPlayer(PlayerState player)
    {
        var index = _players.FindIndex(candidate => candidate.Id == player.Id);
        _players[index] = player;
    }

    public PlayerId Other(PlayerId player) =>
        _players.Single(candidate => candidate.Id != player).Id;

    public CardState Card(CardInstanceId id) => _cards.Single(card => card.Id == id);

    public CardState? FindCard(CardInstanceId id) => _cards.SingleOrDefault(card => card.Id == id);

    public IEnumerable<CardState> CardsIn(PlayerId player, CardZone zone) =>
        _cards
            .Where(card => card.Owner == player && card.Zone == zone)
            .OrderBy(static card => card.StackPosition)
            .ThenBy(static card => card.Id);

    public CardState? Oche(PlayerId player) =>
        _cards.SingleOrDefault(card => card.Owner == player && card.Zone == CardZone.Oche);

    public void SetCard(CardState card)
    {
        var index = _cards.FindIndex(candidate => candidate.Id == card.Id);
        _cards[index] = card;
    }

    public void MoveCard(CardInstanceId id, CardZone zone, CardInstanceId? attachedTo = null)
    {
        var card = Card(id);
        SetCard(
            card with
            {
                Zone = zone,
                IsFaceDown = zone == CardZone.BarChit,
                StackPosition = -1,
                AttachedTo = attachedTo,
            }
        );
        Events.Add(
            new PendingMatchEvent(
                MatchEventKind.CardMoved,
                card.Owner,
                id,
                FrozenList<CardInstanceId>.Create(id)
            )
        );
    }

    public FrozenList<CardInstanceId> Draw(PlayerId player, int count, DrawReason reason)
    {
        var drawn = CardsIn(player, CardZone.Stack)
            .Take(count)
            .Select(static card => card.Id)
            .ToArray();
        foreach (var id in drawn)
        {
            MoveCard(id, CardZone.Mitt);
        }

        if (drawn.Length > 0)
        {
            Events.Add(
                new PendingMatchEvent(
                    MatchEventKind.CardsDrawn,
                    player,
                    TargetCards: FrozenList<CardInstanceId>.Create(drawn),
                    DrawReason: reason,
                    Amount: drawn.Length
                )
            );
        }

        return FrozenList<CardInstanceId>.Create(drawn);
    }

    public void Shuffle(PlayerId player, FrozenList<CardInstanceId> excludedCards = default)
    {
        var stack = CardsIn(player, CardZone.Stack)
            .Where(card => !excludedCards.Contains(card.Id))
            .ToArray();
        for (var index = stack.Length - 1; index > 0; index--)
        {
            var swapIndex = Random.NextInt(index + 1);
            (stack[index], stack[swapIndex]) = (stack[swapIndex], stack[index]);
        }

        for (var index = 0; index < stack.Length; index++)
        {
            SetCard(stack[index] with { StackPosition = index });
        }

        Events.Add(new PendingMatchEvent(MatchEventKind.CardsShuffled, player));
    }

    public void ReturnMittToStack(PlayerId player)
    {
        var nextPosition = CardsIn(player, CardZone.Stack).Count();
        foreach (var card in CardsIn(player, CardZone.Mitt).ToArray())
        {
            SetCard(card with { Zone = CardZone.Stack, StackPosition = nextPosition++ });
        }
    }

    public void SetAsideBarChits(PlayerId player, int count)
    {
        var cards = CardsIn(player, CardZone.Stack).Take(count).ToArray();
        for (var index = 0; index < cards.Length; index++)
        {
            MoveCard(cards[index].Id, CardZone.BarChit);
            SetCard(Card(cards[index].Id) with { StackPosition = index });
        }

        var current = Player(player);
        SetPlayer(current with { BarChitsRemaining = cards.Length });
    }

    public FrozenList<CardInstanceId> TakeBarChits(
        PlayerId player,
        int count,
        CardInstanceId source
    )
    {
        var cards = CardsIn(player, CardZone.BarChit).Take(count).ToArray();
        foreach (var card in cards)
        {
            MoveCard(card.Id, CardZone.Mitt);
        }

        var current = Player(player);
        SetPlayer(current with { BarChitsRemaining = current.BarChitsRemaining - cards.Length });
        Events.Add(
            new PendingMatchEvent(
                MatchEventKind.BarChitsTaken,
                player,
                source,
                FrozenList<CardInstanceId>.Create(cards.Select(static card => card.Id)),
                Amount: cards.Length
            )
        );
        return FrozenList<CardInstanceId>.Create(cards.Select(static card => card.Id));
    }

    public void ResetBarChits(PlayerId player, int count)
    {
        var nextPosition = CardsIn(player, CardZone.Stack).Count();
        foreach (var card in CardsIn(player, CardZone.BarChit).ToArray())
        {
            MoveCard(card.Id, CardZone.Stack);
            SetCard(Card(card.Id) with { StackPosition = nextPosition++ });
        }

        Shuffle(player);
        SetAsideBarChits(player, count);
    }

    public void Attach(CardInstanceId attachmentId, CardInstanceId targetId)
    {
        var target = Card(targetId);
        MoveCard(attachmentId, CardZone.Attached, targetId);
        SetCard(
            target with
            {
                Attachments = FrozenList<CardInstanceId>.Create(
                    target.Attachments.Append(attachmentId)
                ),
            }
        );
    }

    public void DetachTo(CardInstanceId attachmentId, CardZone zone)
    {
        var attachment = Card(attachmentId);
        if (attachment.AttachedTo is { } targetId)
        {
            var target = Card(targetId);
            SetCard(
                target with
                {
                    Attachments = FrozenList<CardInstanceId>.Create(
                        target.Attachments.Where(id => id != attachmentId)
                    ),
                }
            );
        }

        MoveCard(attachmentId, zone);
    }

    public void PlaceDamage(
        PlayerId actor,
        CardInstanceId targetId,
        int damage,
        DamageKind kind,
        CardInstanceId? source = null
    )
    {
        if (damage <= 0)
        {
            return;
        }

        var target = Card(targetId);
        SetCard(target with { Damage = target.Damage + damage });
        Events.Add(
            new PendingMatchEvent(
                MatchEventKind.DamagePlaced,
                actor,
                source,
                FrozenList<CardInstanceId>.Create(targetId),
                DamageKind: kind,
                Amount: damage
            )
        );
    }

    public void Heal(
        PlayerId actor,
        CardInstanceId targetId,
        int amount,
        CardInstanceId? source = null
    )
    {
        var target = Card(targetId);
        var healed = Math.Min(amount, target.Damage);
        if (healed <= 0)
        {
            return;
        }

        SetCard(target with { Damage = target.Damage - healed });
        Events.Add(
            new PendingMatchEvent(
                MatchEventKind.DamageHealed,
                actor,
                source,
                FrozenList<CardInstanceId>.Create(targetId),
                Amount: healed
            )
        );
    }

    public void ApplyRoughState(
        PlayerId actor,
        CardInstanceId targetId,
        BlokemonRoughState state,
        CardInstanceId? source = null
    )
    {
        var target = Card(targetId);
        if (
            target.Zone != CardZone.Oche
            || (target.Kind == CardKind.Kit && _catalog.IsFossil(target.MechanicalId))
        )
        {
            return;
        }

        var states = target.RoughStates.Where(entry => entry.State != state).ToList();
        if (_catalog.Manifest.BaseRules.RoughStateCoexistence.RotatedGroup.Contains(state))
        {
            states.RemoveAll(entry =>
                _catalog.Manifest.BaseRules.RoughStateCoexistence.RotatedGroup.Contains(entry.State)
            );
        }

        states.Add(new RoughStateEntry(state, Player(target.Owner).RoundsStarted));
        SetCard(target with { RoughStates = FrozenList<RoughStateEntry>.Create(states) });
        Events.Add(
            new PendingMatchEvent(
                MatchEventKind.RoughStateApplied,
                actor,
                source,
                FrozenList<CardInstanceId>.Create(targetId),
                RoughState: state
            )
        );
    }

    public void ClearRoughStates(
        PlayerId actor,
        CardInstanceId targetId,
        BlokemonRoughState? state = null
    )
    {
        var target = Card(targetId);
        var cleared = state is null
            ? target.RoughStates.ToArray()
            : target.RoughStates.Where(entry => entry.State == state).ToArray();
        if (cleared.Length == 0)
        {
            return;
        }

        SetCard(
            target with
            {
                RoughStates = state is null
                    ? []
                    : FrozenList<RoughStateEntry>.Create(
                        target.RoughStates.Where(entry => entry.State != state)
                    ),
            }
        );
        foreach (var entry in cleared)
        {
            Events.Add(
                new PendingMatchEvent(
                    MatchEventKind.RoughStateCleared,
                    actor,
                    TargetCards: FrozenList<CardInstanceId>.Create(targetId),
                    RoughState: entry.State
                )
            );
        }
    }

    public void AddEffect(TemporaryEffect effect)
    {
        _effects.Add(effect);
        Events.Add(
            new PendingMatchEvent(
                MatchEventKind.EffectRegistered,
                effect.Owner,
                effect.SourceCard,
                effect.TargetCard is { } target
                    ? FrozenList<CardInstanceId>.Create(target)
                    : FrozenList<CardInstanceId>.Empty,
                effect.SourceEffect,
                Amount: effect.Amount
            )
        );
    }

    public bool TossBeerMat(PlayerId player, bool applyPlayerEffects = true)
    {
        var badge = Random.NextInt(2) == 1;
        return
            applyPlayerEffects
            && Effects.Any(effect =>
                effect.Owner != player
                && effect.Kind == TemporaryEffectKind.ForceBeerMatBlank
                && effect.AppliesFromRound <= RoundNumber
            )
            ? false
            : badge;
    }

    public void RemoveEffectsFor(CardInstanceId card, bool preserveDelayedTarget = false)
    {
        _effects.RemoveAll(effect =>
            effect.SourceCard == card
                && effect.Kind != TemporaryEffectKind.EndRoundEffect
                && effect.Kind != TemporaryEffectKind.ForceBeerMatBlank
                && effect.Duration != EffectDuration.WhileTargetInPlay
            || effect.TargetCard == card
                && (!preserveDelayedTarget || effect.Kind != TemporaryEffectKind.EndRoundEffect)
        );
    }

    public void RemoveEffects(EffectId sourceEffect, CardInstanceId sourceCard)
    {
        _effects.RemoveAll(effect =>
            effect.SourceEffect == sourceEffect && effect.SourceCard == sourceCard
        );
    }

    public void RemoveEffect(TemporaryEffect effect)
    {
        _effects.Remove(effect);
    }

    public void ExpireEffects(int completedRound)
    {
        _effects.RemoveAll(effect =>
            effect.Duration != EffectDuration.WhileSourceInPlay
            && effect.Duration != EffectDuration.WhileTargetInPlay
            && effect.Duration != EffectDuration.CurrentResolution
            && effect.ExpiresAfterRound <= completedRound
        );
    }

    public FrozenList<CardInstanceId> ChuckBloke(CardInstanceId id)
    {
        var card = Card(id);
        var chucked = card.Attachments.Concat(card.UnderlyingCards).Append(id).Distinct().ToArray();
        foreach (var cardId in chucked)
        {
            var current = Card(cardId);
            SetCard(
                current with
                {
                    Zone = CardZone.EmptiesTray,
                    StackPosition = -1,
                    AttachedTo = null,
                    Attachments = [],
                    UnderlyingCards = [],
                    RoughStates = [],
                }
            );
            RemoveEffectsFor(cardId);
        }

        return FrozenList<CardInstanceId>.Create(chucked);
    }

    public MatchState Snapshot() =>
        new(
            Id,
            AuthorityVersion,
            Seed,
            Random.Snapshot,
            Revision,
            LastEventSequence,
            Phase,
            OpeningPlayer,
            ActivePlayer,
            RoundNumber,
            FrozenList<PlayerState>.Create(_players),
            FrozenList<CardState>.Create(_cards.OrderBy(static card => card.Id)),
            FrozenList<TemporaryEffect>.Create(
                _effects
                    .OrderBy(static effect => effect.SourceEffect.Value, StringComparer.Ordinal)
                    .ThenBy(static effect => effect.SourceCard)
            ),
            FrozenList<CommandId>.Create(_processedCommands),
            RoundUsage,
            PendingEffect,
            PendingKnockout,
            FrozenList<PendingBarChitResolution>.Create(_pendingBarChits),
            ReplacementPlayer,
            PendingRoundEnd,
            Winner,
            SuddenDeathCount
        );
}
