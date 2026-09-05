namespace Blokemon.App

open System
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Blokemon.App.Contracts
open Blokemon.App.TenancyDocuments
open Blokemon.Product

/// One recovery code as it is stored: its hash and, once used, when.
type RecoveryCodeEntry =
    { Hash: string
      ConsumedAt: Nullable<DateTimeOffset> }

/// The account's current recovery-code set, stored at `recovery/{account}`. Only hashes are
/// held; the plain codes leave the server once, in the response that generated them.
type RecoveryCodeDocument =
    { SchemaVersion: int
      Account: string
      GeneratedAt: DateTimeOffset
      Codes: RecoveryCodeEntry array }

/// A code set as it was read, at the revision a change must be written against.
type LoadedRecoveryCodes =
    { Revision: int64
      Document: RecoveryCodeDocument }

/// Why a recovery code was not consumed or a set not issued.
[<RequireQualifiedAccess>]
type RecoveryFailure =
    /// The code is malformed, belongs to no live set, or was consumed already.
    | Refused
    /// The set changed underneath the operation and could not be retried; nothing was written.
    | Conflict
    /// A stored set cannot be read; nothing was written.
    | Damaged

/// Recovery codes: ten per set, 128 bits each from the cryptographic generator, held as hashes,
/// consumed atomically against the set's revision, and the sole identifier a person presents
/// during recovery.
module RecoveryCodes =

    let schemaVersion = 1

    /// The number of codes in a set.
    let SetSize = 10

    /// The entropy of one code, in bytes.
    let CodeBytes = 16

    let key (account: AccountId) = $"recovery/{account}"

    let private hex (bytes: byte[]) = Convert.ToHexStringLower bytes

    /// A code as it is shown: thirty-two hexadecimal characters in four groups of eight.
    let format (bytes: byte[]) =
        let plain = hex bytes

        String.Join(
            "-",
            [| plain.Substring(0, 8)
               plain.Substring(8, 8)
               plain.Substring(16, 8)
               plain.Substring(24, 8) |]
        )

    /// The canonical form of a presented code, or None when it is not a code at all. Grouping
    /// dashes and whitespace are ignored; anything else is not a code.
    let normalize (presented: string | null) : string option =
        match presented with
        | null -> None
        | text ->
            let stripped =
                text
                |> Seq.filter (fun character ->
                    character <> '-' && not (Char.IsWhiteSpace character))
                |> Seq.map Char.ToLowerInvariant
                |> Seq.toArray
                |> String

            if
                stripped.Length = CodeBytes * 2
                && stripped |> Seq.forall (fun character -> Uri.IsHexDigit character)
            then
                Some stripped
            else
                None

    let hash (normalized: string) =
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes normalized))

    let private hashesMatch (stored: string) (presented: string) =
        CryptographicOperations.FixedTimeEquals(
            ReadOnlySpan<byte>(Encoding.UTF8.GetBytes stored),
            ReadOnlySpan<byte>(Encoding.UTF8.GetBytes presented)
        )

    /// A fresh set of plain codes.
    let generate () : string array =
        Array.init SetSize (fun _ -> format (RandomNumberGenerator.GetBytes CodeBytes))

    let private parse (document: StoredDocument) : RecoveryCodeDocument option =
        let parsed =
            try
                Ok(JsonSerializer.Deserialize<RecoveryCodeDocument>(document.Json, json))
            with :? JsonException ->
                Error()

        match parsed with
        | Ok(NonNull value) when
            value.SchemaVersion = schemaVersion
            && not (DamagedDocument.isMissing value.Codes)
            ->
            Some value
        | _ -> None

    let private readKey
        (documents: IStateDocumentStore)
        (key: string)
        (cancellationToken: CancellationToken)
        : Task<DomainResult<LoadedRecoveryCodes option, RecoveryFailure>> =
        task {
            let! stored = documents.Read(key, cancellationToken)

            match stored with
            | null -> return DomainResult.Succeeded None
            | document ->
                match parse document with
                | Some value ->
                    return
                        DomainResult.Succeeded(
                            Some
                                { Revision = document.Revision
                                  Document = value }
                        )
                | None -> return DomainResult.Failed RecoveryFailure.Damaged
        }

    let load
        (documents: IStateDocumentStore)
        (account: AccountId)
        (cancellationToken: CancellationToken)
        : Task<DomainResult<LoadedRecoveryCodes option, RecoveryFailure>> =
        readKey documents (key account) cancellationToken

    /// How many codes of the set are still unused.
    let liveCount (document: RecoveryCodeDocument) =
        document.Codes
        |> Array.filter (fun entry -> not entry.ConsumedAt.HasValue)
        |> Array.length

    /// Whether the account has a set with at least one unused code.
    let hasLive
        (documents: IStateDocumentStore)
        (account: AccountId)
        (cancellationToken: CancellationToken)
        : Task<bool> =
        task {
            let! loaded = load documents account cancellationToken

            match loaded with
            | DomainResult.Succeeded(Some set) -> return liveCount set.Document > 0
            | _ -> return false
        }

    /// Issues a new set for the account, replacing any previous one, and returns the plain
    /// codes: the one time they exist outside the person's own keeping.
    let issue
        (documents: IStateDocumentStore)
        (account: AccountId)
        (now: DateTimeOffset)
        (cancellationToken: CancellationToken)
        : Task<DomainResult<string array, RecoveryFailure>> =
        task {
            let codes = generate ()

            let document =
                { SchemaVersion = schemaVersion
                  Account = account.Value
                  GeneratedAt = now
                  Codes =
                    codes
                    |> Array.map (fun code ->
                        { Hash = hash (Option.get (normalize code))
                          ConsumedAt = Nullable() }) }

            let serialized = JsonSerializer.Serialize(document, json)
            let! existing = load documents account cancellationToken

            match existing with
            | DomainResult.Failed failure -> return DomainResult.Failed failure
            | DomainResult.Succeeded None ->
                let! write = documents.Create(key account, serialized, cancellationToken)

                match write with
                | :? DocumentWriteResult.Written -> return DomainResult.Succeeded codes
                | _ -> return DomainResult.Failed RecoveryFailure.Conflict
            | DomainResult.Succeeded(Some loaded) ->
                let! write =
                    documents.Update(key account, loaded.Revision, serialized, cancellationToken)

                match write with
                | :? DocumentWriteResult.Written -> return DomainResult.Succeeded codes
                | _ -> return DomainResult.Failed RecoveryFailure.Conflict
        }

    /// The index of the unused entry matching the presented hash, comparing every entry so the
    /// time taken does not say which one matched.
    let private matching (document: RecoveryCodeDocument) (presented: string) =
        document.Codes
        |> Array.mapi (fun index entry -> index, entry)
        |> Array.fold
            (fun (found: int option) (index, entry) ->
                let matches = hashesMatch entry.Hash presented

                if matches && not entry.ConsumedAt.HasValue && found.IsNone then
                    Some index
                else
                    found)
            None

    let private consumeAt
        (documents: IStateDocumentStore)
        (key: string)
        (loaded: LoadedRecoveryCodes)
        (index: int)
        (now: DateTimeOffset)
        (cancellationToken: CancellationToken)
        =
        let document = loaded.Document

        let consumed =
            { document with
                Codes =
                    document.Codes
                    |> Array.mapi (fun position entry ->
                        if position = index then
                            { entry with ConsumedAt = Nullable now }
                        else
                            entry) }

        documents.Update(
            key,
            loaded.Revision,
            JsonSerializer.Serialize(consumed, json),
            cancellationToken
        )

    /// The set the presented code belongs to and the index of the unused entry it matches,
    /// or None. The code alone locates the set: every `recovery/` document is read and its
    /// entries compared in fixed time. Nothing is written.
    let locate
        (documents: IStateDocumentStore)
        (listing: IDocumentListing)
        (presented: string | null)
        (cancellationToken: CancellationToken)
        : Task<(string * LoadedRecoveryCodes * int) option> =
        task {
            match normalize presented with
            | None -> return None
            | Some normalized ->
                let presentedHash = hash normalized
                let! summaries = listing.List("recovery/", cancellationToken)
                let mutable found = None

                for summary in summaries do
                    if found.IsNone then
                        let! loaded = readKey documents summary.Key cancellationToken

                        match loaded with
                        | DomainResult.Succeeded(Some set) ->
                            match matching set.Document presentedHash with
                            | Some index -> found <- Some(summary.Key, set, index)
                            | None -> ()
                        | _ -> ()

                return found
        }

    /// Consumes the located code against the revision its set was read at, so two concurrent
    /// uses of one code succeed exactly once; a race on a different code of the same set is
    /// re-read and retried.
    let consumeFrom
        (documents: IStateDocumentStore)
        (key: string)
        (located: LoadedRecoveryCodes)
        (index: int)
        (now: DateTimeOffset)
        (cancellationToken: CancellationToken)
        : Task<DomainResult<unit, RecoveryFailure>> =
        task {
            let entryHash = located.Document.Codes[index].Hash
            let mutable current = Some located
            let mutable attempts = 0
            let mutable outcome = DomainResult.Failed RecoveryFailure.Conflict

            while current.IsSome && attempts < 3 do
                attempts <- attempts + 1
                let set = Option.get current

                match matching set.Document entryHash with
                | None ->
                    outcome <- DomainResult.Failed RecoveryFailure.Refused
                    current <- None
                | Some position ->
                    let! write = consumeAt documents key set position now cancellationToken

                    match write with
                    | :? DocumentWriteResult.Written ->
                        outcome <- DomainResult.Succeeded()
                        current <- None
                    | _ ->
                        let! again = readKey documents key cancellationToken

                        match again with
                        | DomainResult.Succeeded(Some reread) -> current <- Some reread
                        | DomainResult.Succeeded None ->
                            outcome <- DomainResult.Failed RecoveryFailure.Refused
                            current <- None
                        | DomainResult.Failed failure ->
                            outcome <- DomainResult.Failed failure
                            current <- None

            return outcome
        }
