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

/// The one redemption of the operator bootstrap code, stored at `operator-bootstrap`.
type OperatorBootstrapDocument =
    { SchemaVersion: int
      Account: string
      RedeemedAt: DateTimeOffset }

/// Why the bootstrap code was not redeemed.
[<RequireQualifiedAccess>]
type OperatorBootstrapFailure =
    /// The deployment configured no code.
    | NotConfigured
    /// The session is not a FirstParty session.
    | FirstPartyRequired
    /// The code was redeemed already.
    | Redeemed
    /// The code presented is not the configured one.
    | Refused
    /// The account record cannot be read.
    | Damaged
    /// The account record changed underneath the redemption.
    | Conflict

/// Redeems the deployment's operator bootstrap code exactly once, from a FirstParty session,
/// compared in constant time; the redemption is recorded and the account flagged operator.
module OperatorBootstrap =

    [<Literal>]
    let Key = "operator-bootstrap"

    let schemaVersion = 1

    let toError (failure: OperatorBootstrapFailure) : ApiError =
        match failure with
        | OperatorBootstrapFailure.NotConfigured ->
            ApiError("bootstrap.unavailable", "This server has no operator bootstrap code.")
        | OperatorBootstrapFailure.FirstPartyRequired ->
            ApiError("bootstrap.provenance", "Operator bootstrap needs a first-party sign-in.")
        | OperatorBootstrapFailure.Redeemed ->
            ApiError("bootstrap.redeemed", "The operator bootstrap code was already redeemed.")
        | OperatorBootstrapFailure.Refused ->
            ApiError("bootstrap.refused", "That is not the operator bootstrap code.")
        | OperatorBootstrapFailure.Damaged ->
            ApiError("bootstrap.damaged", "The account record could not be read. Nothing changed.")
        | OperatorBootstrapFailure.Conflict ->
            ApiError(
                "bootstrap.conflict",
                "The account changed underneath this request. Try again."
            )

    /// The refusal a locked-out client receives before any comparison.
    let locked () =
        ApiError("bootstrap.locked", "Too many attempts. Try again in fifteen minutes.")

    /// Both sides are hashed to one length before the fixed-time comparison, so neither the
    /// outcome nor the timing depends on how much of the code was right.
    let codesMatch (configured: string) (presented: string) =
        CryptographicOperations.FixedTimeEquals(
            ReadOnlySpan<byte>(SHA256.HashData(Encoding.UTF8.GetBytes configured)),
            ReadOnlySpan<byte>(SHA256.HashData(Encoding.UTF8.GetBytes presented))
        )

    let private flagOperator
        (documents: IStateDocumentStore)
        (account: AccountId)
        (cancellationToken: CancellationToken)
        =
        task {
            let mutable attempts = 0
            let mutable outcome = DomainResult.Failed OperatorBootstrapFailure.Conflict

            while attempts < 3 && not outcome.IsSucceeded do
                attempts <- attempts + 1
                let! stored = documents.Read(accountKey account, cancellationToken)

                match stored with
                | null -> outcome <- DomainResult.Failed OperatorBootstrapFailure.Damaged
                | document ->
                    let parsed =
                        try
                            Ok(JsonSerializer.Deserialize<AccountDocument>(document.Json, json))
                        with :? JsonException ->
                            Error()

                    match parsed with
                    | Ok(NonNull value) when value.SchemaVersion = accountSchemaVersion ->
                        if value.Operator then
                            outcome <- DomainResult.Succeeded()
                        else
                            let! write =
                                documents.Update(
                                    accountKey account,
                                    document.Revision,
                                    JsonSerializer.Serialize({ value with Operator = true }, json),
                                    cancellationToken
                                )

                            match write with
                            | :? DocumentWriteResult.Written -> outcome <- DomainResult.Succeeded()
                            | _ -> ()
                    | _ ->
                        outcome <- DomainResult.Failed OperatorBootstrapFailure.Damaged
                        attempts <- 3

            return outcome
        }

    let redeem
        (documents: IStateDocumentStore)
        (configuredCode: string | null)
        (session: Session)
        (presented: string | null)
        (now: DateTimeOffset)
        (cancellationToken: CancellationToken)
        : Task<DomainResult<DateTimeOffset, OperatorBootstrapFailure>> =
        task {
            match configuredCode with
            | null -> return DomainResult.Failed OperatorBootstrapFailure.NotConfigured
            | configured ->
                if session.Provenance <> SessionProvenance.FirstParty then
                    return DomainResult.Failed OperatorBootstrapFailure.FirstPartyRequired
                else
                    let! existing = documents.Read(Key, cancellationToken)

                    match existing with
                    | NonNull _ -> return DomainResult.Failed OperatorBootstrapFailure.Redeemed
                    | Null ->
                        let presented =
                            match presented with
                            | null -> ""
                            | text -> text

                        if not (codesMatch configured presented) then
                            return DomainResult.Failed OperatorBootstrapFailure.Refused
                        else
                            let record =
                                { SchemaVersion = schemaVersion
                                  Account = session.Account.Value
                                  RedeemedAt = now }

                            let! write =
                                documents.Create(
                                    Key,
                                    JsonSerializer.Serialize(record, json),
                                    cancellationToken
                                )

                            match write with
                            | :? DocumentWriteResult.Written ->
                                let! flagged =
                                    flagOperator documents session.Account cancellationToken

                                match flagged with
                                | DomainResult.Succeeded() -> return DomainResult.Succeeded now
                                | DomainResult.Failed failure -> return DomainResult.Failed failure
                            | _ -> return DomainResult.Failed OperatorBootstrapFailure.Redeemed
        }
