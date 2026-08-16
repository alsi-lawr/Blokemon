namespace Blokemon.Product;

public enum TextValueFailure
{
    Required,
}

public sealed record ProfileId
{
    private ProfileId(string value) => Value = value;

    public string Value { get; }

    public static DomainResult<ProfileId, TextValueFailure> Create(string? value) =>
        NonBlankText.Create(value, static valid => new ProfileId(valid));

    public override string ToString() => Value;
}

public sealed record DisplayName
{
    public const int MaximumLength = 32;

    private DisplayName(string value) => Value = value;

    public string Value { get; }

    public static DomainResult<DisplayName, DisplayNameCreationFailure> Create(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return DomainResult<DisplayName, DisplayNameCreationFailure>.Failure(
                DisplayNameCreationFailure.Required
            );
        }

        return trimmed.Length > MaximumLength
            ? DomainResult<DisplayName, DisplayNameCreationFailure>.Failure(
                DisplayNameCreationFailure.TooLong
            )
            : DomainResult<DisplayName, DisplayNameCreationFailure>.Success(
                new DisplayName(trimmed)
            );
    }

    public override string ToString() => Value;
}

public enum DisplayNameCreationFailure
{
    Required,
    TooLong,
}

public sealed record CommandId
{
    private CommandId(string value) => Value = value;

    public string Value { get; }

    public static DomainResult<CommandId, TextValueFailure> Create(string? value) =>
        NonBlankText.Create(value, static valid => new CommandId(valid));

    public override string ToString() => Value;
}

public sealed record PackReceiptId
{
    private PackReceiptId(string value) => Value = value;

    public string Value { get; }

    public static DomainResult<PackReceiptId, TextValueFailure> Create(string? value) =>
        NonBlankText.Create(value, static valid => new PackReceiptId(valid));

    public override string ToString() => Value;
}

public sealed record DeckId
{
    private DeckId(string value) => Value = value;

    public string Value { get; }

    public static DomainResult<DeckId, TextValueFailure> Create(string? value) =>
        NonBlankText.Create(value, static valid => new DeckId(valid));

    public override string ToString() => Value;
}

public sealed record StarterDeckId
{
    private StarterDeckId(string value) => Value = value;

    public string Value { get; }

    public static DomainResult<StarterDeckId, TextValueFailure> Create(string? value) =>
        NonBlankText.Create(value, static valid => new StarterDeckId(valid));

    public override string ToString() => Value;
}

public sealed record DeckName
{
    private DeckName(string value) => Value = value;

    public string Value { get; }

    public static DomainResult<DeckName, TextValueFailure> Create(string? value) =>
        NonBlankText.Create(value, static valid => new DeckName(valid));

    public override string ToString() => Value;
}

public sealed record CardId
{
    private CardId(string value) => Value = value;

    public string Value { get; }

    public static DomainResult<CardId, TextValueFailure> Create(string? value) =>
        NonBlankText.Create(value, static valid => new CardId(valid));

    internal static CardId FromAuthority(string value) => new(value);

    public override string ToString() => Value;
}

internal static class NonBlankText
{
    internal static DomainResult<TValue, TextValueFailure> Create<TValue>(
        string? value,
        Func<string, TValue> create
    )
        where TValue : notnull =>
        string.IsNullOrWhiteSpace(value)
            ? DomainResult<TValue, TextValueFailure>.Failure(TextValueFailure.Required)
            : DomainResult<TValue, TextValueFailure>.Success(create(value));
}
