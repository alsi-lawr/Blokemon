namespace Blokemon.Product

open System

/// Why a piece of required text was rejected.
type TextValueFailure =
    | Required = 0

/// Why a display name was rejected.
type DisplayNameCreationFailure =
    | Required = 0
    | TooLong = 1

/// Every identity below is the same shape: non-blank text, or a typed failure.
module internal NonBlankText =

    let create
        (value: string | null)
        (make: string -> 'TValue)
        : DomainResult<'TValue, TextValueFailure> =
        match value with
        | null -> DomainResult.Failed TextValueFailure.Required
        | text when String.IsNullOrWhiteSpace text -> DomainResult.Failed TextValueFailure.Required
        | text -> DomainResult.Succeeded(make text)

/// Identifies a local profile.
type ProfileId =
    private
        { value: string }

    member this.Value = this.value

    override this.ToString() = this.value

    static member op_Equality(left: ProfileId, right: ProfileId) = left.Equals(right)

    static member op_Inequality(left: ProfileId, right: ProfileId) = not (left.Equals(right))

    static member Create(value: string | null) =
        NonBlankText.create value (fun valid -> { value = valid })

/// The name a player chose for themselves.
type DisplayName =
    private
        { value: string }

    /// The longest display name the game accepts.
    static member MaximumLength = 32

    member this.Value = this.value

    override this.ToString() = this.value

    static member op_Equality(left: DisplayName, right: DisplayName) = left.Equals(right)

    static member op_Inequality(left: DisplayName, right: DisplayName) = not (left.Equals(right))

    static member Create
        (value: string | null)
        : DomainResult<DisplayName, DisplayNameCreationFailure> =
        let trimmed =
            match value with
            | null -> null
            | text -> text.Trim()

        match trimmed with
        | null -> DomainResult.Failed DisplayNameCreationFailure.Required
        | "" -> DomainResult.Failed DisplayNameCreationFailure.Required
        | text when text.Length > DisplayName.MaximumLength ->
            DomainResult.Failed DisplayNameCreationFailure.TooLong
        | text -> DomainResult.Succeeded { value = text }

/// Identifies the client request that caused a change.
type CommandId =
    private
        { value: string }

    member this.Value = this.value

    override this.ToString() = this.value

    static member op_Equality(left: CommandId, right: CommandId) = left.Equals(right)

    static member op_Inequality(left: CommandId, right: CommandId) = not (left.Equals(right))

    static member Create(value: string | null) =
        NonBlankText.create value (fun valid -> { value = valid })

/// Identifies one opened pack.
type PackReceiptId =
    private
        { value: string }

    member this.Value = this.value

    override this.ToString() = this.value

    static member op_Equality(left: PackReceiptId, right: PackReceiptId) = left.Equals(right)

    static member op_Inequality(left: PackReceiptId, right: PackReceiptId) =
        not (left.Equals(right))

    static member Create(value: string | null) =
        NonBlankText.create value (fun valid -> { value = valid })

/// Identifies a saved deck.
type DeckId =
    private
        { value: string }

    member this.Value = this.value

    override this.ToString() = this.value

    static member op_Equality(left: DeckId, right: DeckId) = left.Equals(right)

    static member op_Inequality(left: DeckId, right: DeckId) = not (left.Equals(right))

    static member Create(value: string | null) =
        NonBlankText.create value (fun valid -> { value = valid })

/// Identifies a starter deck in the catalogue.
type StarterDeckId =
    private
        { value: string }

    member this.Value = this.value

    override this.ToString() = this.value

    static member op_Equality(left: StarterDeckId, right: StarterDeckId) = left.Equals(right)

    static member op_Inequality(left: StarterDeckId, right: StarterDeckId) =
        not (left.Equals(right))

    static member Create(value: string | null) =
        NonBlankText.create value (fun valid -> { value = valid })

/// The name a player gave a saved deck.
type DeckName =
    private
        { value: string }

    member this.Value = this.value

    override this.ToString() = this.value

    static member op_Equality(left: DeckName, right: DeckName) = left.Equals(right)

    static member op_Inequality(left: DeckName, right: DeckName) = not (left.Equals(right))

    static member Create(value: string | null) =
        NonBlankText.create value (fun valid -> { value = valid })

/// Identifies a card in the mechanical authority.
type CardId =
    private
        { value: string }

    member this.Value = this.value

    override this.ToString() = this.value

    static member op_Equality(left: CardId, right: CardId) = left.Equals(right)

    static member op_Inequality(left: CardId, right: CardId) = not (left.Equals(right))

    static member Create(value: string | null) =
        NonBlankText.create value (fun valid -> { value = valid })

    static member internal FromAuthority(value: string) = { value = value }
