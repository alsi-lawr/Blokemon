using System.Text.Json.Serialization;
using Blokemon.Core.SetDesign;

namespace Blokemon.Core.PublicContent;

public enum BlokemonPublicContentApprovalStatus
{
    AwaitingApproval,
}

public enum BlokemonPublicTermCategory
{
    Type,
    Stage,
    Category,
    Status,
    Target,
    Choice,
    Quantity,
    Cost,
    Timing,
    Core,
    BattleTiming,
}

public sealed record BlokemonPublicTerm(
    string Id,
    BlokemonPublicTermCategory Category,
    string Singular,
    string Plural,
    string Definition
);

public sealed record BlokemonPublicEffect(
    string MechanicalId,
    string Name,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? EffectText = null
);

public sealed record BlokemonPublicIllustration(string Brief, string Prompt, string AltIntent);

public enum BlokemonPublicArtStatus
{
    Placeholder,
}

public sealed record BlokemonPublicArtReference(BlokemonPublicArtStatus Status, string Authority);

public sealed record BlokemonPublicCollectible(
    string Id,
    string ApprovedName,
    BlokemonApprovedType ApprovedType,
    string FlavourText,
    BlokemonPublicEffect[] Abilities,
    BlokemonPublicEffect[] Attacks,
    BlokemonPublicEffect[] Rules,
    BlokemonPublicIllustration Illustration,
    BlokemonPublicArtReference Art
);

public sealed record BlokemonPublicSupport(
    string Id,
    string Name,
    string CategoryTermId,
    BlokemonPublicEffect[] Effects
);

public sealed record BlokemonPublicBasicEnergy(
    string Id,
    string Name,
    string Definition,
    string SymbolKey,
    string AccessibleLabel
);

public sealed record BlokemonPublicContentManifest(
    string SchemaVersion,
    string ContentVersion,
    string MechanicalManifestVersion,
    string TerminologyVersion,
    BlokemonPublicContentApprovalStatus HumanApprovalStatus,
    BlokemonPublicTerm[] Terminology,
    BlokemonPublicCollectible[] Collectibles,
    BlokemonPublicSupport[] Supports,
    BlokemonPublicBasicEnergy[] BasicEnergy
);

public sealed record BlokemonPublicContentIssue(string Code, string Message);

public sealed record BlokemonPublicContentValidation(BlokemonPublicContentIssue[] Issues)
{
    public bool IsValid => Issues.Length == 0;
}
