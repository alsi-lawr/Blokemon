using Blokemon.Core.SetDesign;

namespace Blokemon.Game;

internal sealed class AuthorityCatalog
{
    private readonly Dictionary<string, BlokemonCollectible> _collectibles;
    private readonly Dictionary<string, BlokemonKit> _kits;
    private readonly Dictionary<string, BlokemonBasicVim> _vim;
    private readonly Dictionary<string, BlokemonAttack> _attacks;
    private readonly Dictionary<string, BlokemonPartyTrick> _partyTricks;
    private readonly Dictionary<string, BlokemonHouseRule> _houseRules;

    public AuthorityCatalog(BlokemonRuntimeManifest manifest)
    {
        Manifest = manifest;
        _collectibles = manifest.Collectibles.ToDictionary(static card => card.Id);
        _kits = manifest.Kits.ToDictionary(static card => card.Id);
        _vim = manifest.BasicVim.ToDictionary(static card => card.Id);
        _attacks = manifest
            .Collectibles.SelectMany(static card => card.Attacks)
            .Concat(manifest.Kits.SelectMany(static card => card.Attacks))
            .ToDictionary(static effect => effect.MechanicalId);
        _partyTricks = manifest
            .Collectibles.SelectMany(static card => card.PartyTricks)
            .Concat(manifest.Kits.SelectMany(static card => card.PartyTricks))
            .ToDictionary(static effect => effect.MechanicalId);
        _houseRules = manifest
            .Collectibles.SelectMany(static card => card.HouseRules)
            .Concat(manifest.Kits.SelectMany(static card => card.HouseRules))
            .ToDictionary(static effect => effect.MechanicalId);
    }

    public BlokemonRuntimeManifest Manifest { get; }

    public bool Contains(MechanicalCardId id) =>
        _collectibles.ContainsKey(id.Value)
        || _kits.ContainsKey(id.Value)
        || _vim.ContainsKey(id.Value);

    public CardKind Kind(MechanicalCardId id) =>
        _collectibles.ContainsKey(id.Value) ? CardKind.Bloke
        : _kits.ContainsKey(id.Value) ? CardKind.Kit
        : CardKind.Vim;

    public int CopyLimit(MechanicalCardId id) =>
        _collectibles.TryGetValue(id.Value, out var bloke) ? bloke.StackCopyLimit
        : _kits.TryGetValue(id.Value, out var kit) ? kit.StackCopyLimit
        : _vim[id.Value].StackCopyLimit;

    public bool IsRegular(MechanicalCardId id) =>
        _collectibles.TryGetValue(id.Value, out var card) && card.Rank == BlokemonRank.Regular;

    public bool IsFossil(MechanicalCardId id) =>
        Manifest.BaseRules.FossilKits.KitIds.Contains(id.Value, StringComparer.Ordinal);

    public BlokemonCollectible Bloke(MechanicalCardId id) => _collectibles[id.Value];

    public BlokemonKit Kit(MechanicalCardId id) => _kits[id.Value];

    public BlokemonBasicVim Vim(MechanicalCardId id) => _vim[id.Value];

    public BlokemonAttack? Attack(EffectId id) => _attacks.GetValueOrDefault(id.Value);

    public BlokemonPartyTrick? PartyTrick(EffectId id) => _partyTricks.GetValueOrDefault(id.Value);

    public BlokemonHouseRule? HouseRule(EffectId id) => _houseRules.GetValueOrDefault(id.Value);

    public int StayingPower(CardState card) =>
        card.Kind == CardKind.Bloke
            ? Bloke(card.MechanicalId).StayingPower
            : Manifest.BaseRules.FossilKits.PlayAsRegularLocalStayingPower;

    public int TaxiFare(CardState card) =>
        card.Kind == CardKind.Bloke ? Bloke(card.MechanicalId).TaxiFare : int.MaxValue;

    public int BarChits(CardState card) =>
        card.Kind == CardKind.Bloke ? Bloke(card.MechanicalId).BarChitsWhenSentHome
        : Manifest.BaseRules.FossilKits.SentHomeAwardsOneBarChit ? 1
        : 0;

    public FrozenList<BlokemonMechanicalType> MechanicalTypes(CardState card) =>
        card.Kind == CardKind.Bloke
            ? FrozenList<BlokemonMechanicalType>.Create(Bloke(card.MechanicalId).MechanicalTypes)
            : FrozenList<BlokemonMechanicalType>.Create(BlokemonMechanicalType.Colorless);

    public IEnumerable<BlokemonPartyTrick> PartyTricks(CardState card) =>
        card.Kind == CardKind.Bloke ? Bloke(card.MechanicalId).PartyTricks
        : card.Kind == CardKind.Kit ? Kit(card.MechanicalId).PartyTricks
        : [];

    public IEnumerable<BlokemonAttack> Attacks(CardState card) =>
        card.Kind == CardKind.Bloke ? Bloke(card.MechanicalId).Attacks
        : card.Kind == CardKind.Kit ? Kit(card.MechanicalId).Attacks
        : [];

    public IEnumerable<BlokemonHouseRule> HouseRules(CardState card) =>
        card.Kind == CardKind.Bloke ? Bloke(card.MechanicalId).HouseRules
        : card.Kind == CardKind.Kit ? Kit(card.MechanicalId).HouseRules
        : [];
}
