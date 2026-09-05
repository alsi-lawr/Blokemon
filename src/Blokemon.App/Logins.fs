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

/// The account's simple login, stored at `login/{account}`: the player name as typed and the
/// verifier of its password. The password itself is never stored.
type LoginDocument =
    { SchemaVersion: int
      Account: string
      Name: string
      NormalizedName: string
      PasswordHash: string
      SetAt: DateTimeOffset }

/// The index from a login name to its account, stored at `loginname/{hash of the normalized
/// name}`: the key stays fixed literals and hexadecimal, and the store's create-once rule on it
/// is what makes the name unique without regard to case.
type LoginNameDocument =
    { SchemaVersion: int
      NormalizedName: string
      Account: string }

/// A login as it was read, at the revision a change must be written against.
type LoadedLogin =
    { Revision: int64
      Document: LoginDocument }

/// Why a password was rejected before anything was hashed.
type PasswordFailure =
    | Required = 0
    | TooShort = 1
    | TooLong = 2

/// Why a login was not created, changed or accepted.
[<RequireQualifiedAccess>]
type LoginFailure =
    /// The name is not a login name; the typed failure says why.
    | Name of LoginNameFailure
    /// The password is outside the bounds; the typed failure says why.
    | Password of PasswordFailure
    /// Another account already has this name, without regard to case; nothing changed.
    | NameTaken
    /// The account already has a login name and a different one was given; nothing changed.
    | AlreadyNamed
    /// The account has no login name and none was given; nothing changed.
    | NameRequired
    /// The name and password do not match an account. Whether the name exists is not said.
    | Refused
    /// A record changed underneath the write; nothing changed.
    | Conflict
    /// A stored login cannot be read; nothing changed.
    | Damaged

/// Passwords as the login accepts them: any text within the bounds, hashed with PBKDF2 over
/// HMAC-SHA-512 from the base class library, a random salt per password and the iteration count
/// recorded beside the hash so a later count verifies older hashes.
module Passwords =

    let MinimumLength = 8

    /// A bound on the work one request can ask for, not a limit anyone reaches.
    let MaximumLength = 128

    /// The current iteration count; recorded in every hash.
    let Iterations = 210_000

    let SaltBytes = 16

    let HashBytes = 32

    [<Literal>]
    let private Algorithm = "pbkdf2-sha512"

    let validate (password: string | null) : DomainResult<string, PasswordFailure> =
        match password with
        | null -> DomainResult.Failed PasswordFailure.Required
        | "" -> DomainResult.Failed PasswordFailure.Required
        | text when text.Length < MinimumLength -> DomainResult.Failed PasswordFailure.TooShort
        | text when text.Length > MaximumLength -> DomainResult.Failed PasswordFailure.TooLong
        | text -> DomainResult.Succeeded text

    let private derive (password: string) (salt: byte[]) (iterations: int) =
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes password,
            salt,
            iterations,
            HashAlgorithmName.SHA512,
            HashBytes
        )

    /// `pbkdf2-sha512$iterations$salt$hash`, the salt and hash in base64.
    let hash (password: string) =
        let salt = RandomNumberGenerator.GetBytes SaltBytes
        let derived = derive password salt Iterations

        String.Join(
            "$",
            [| Algorithm
               string Iterations
               Convert.ToBase64String salt
               Convert.ToBase64String derived |]
        )

    /// Whether the password is the one the stored hash was made from. A hash that is not in
    /// the recorded form verifies nothing.
    let verify (stored: string) (password: string) =
        match stored.Split '$' with
        | [| algorithm; iterations; salt; expected |] when algorithm = Algorithm ->
            match Int32.TryParse iterations with
            | true, count when count > 0 ->
                try
                    let derived = derive password (Convert.FromBase64String salt) count

                    CryptographicOperations.FixedTimeEquals(
                        ReadOnlySpan<byte>(derived),
                        ReadOnlySpan<byte>(Convert.FromBase64String expected)
                    )
                with :? FormatException ->
                    false
            | _ -> false
        | _ -> false

/// Simple logins: a unique player name and a password on an account, beside or instead of its
/// passkeys. Registering reserves the name through the index before the login is written;
/// verifying finds the account through the index and checks the password in fixed time; setting
/// changes the password, taking a name only when the account has none.
module Logins =

    let schemaVersion = 1

    let key (account: AccountId) = $"login/{account}"

    let nameKey (name: LoginName) =
        $"loginname/{DocumentIdentity.ofText name.Normalized}"

    let private parse (document: StoredDocument) : LoginDocument option =
        let parsed =
            try
                Ok(JsonSerializer.Deserialize<LoginDocument>(document.Json, json))
            with :? JsonException ->
                Error()

        match parsed with
        | Ok(NonNull value) when value.SchemaVersion = schemaVersion -> Some value
        | _ -> None

    let private parseName (document: StoredDocument) : LoginNameDocument option =
        let parsed =
            try
                Ok(JsonSerializer.Deserialize<LoginNameDocument>(document.Json, json))
            with :? JsonException ->
                Error()

        match parsed with
        | Ok(NonNull value) when value.SchemaVersion = schemaVersion -> Some value
        | _ -> None

    /// The account's login, if it has one.
    let forAccount
        (documents: IStateDocumentStore)
        (account: AccountId)
        (cancellationToken: CancellationToken)
        : Task<DomainResult<LoadedLogin option, LoginFailure>> =
        task {
            let! stored = documents.Read(key account, cancellationToken)

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
                | None -> return DomainResult.Failed LoginFailure.Damaged
        }

    /// Whether the account has a login.
    let anyFor
        (documents: IStateDocumentStore)
        (account: AccountId)
        (cancellationToken: CancellationToken)
        : Task<bool> =
        task {
            let! loaded = forAccount documents account cancellationToken

            match loaded with
            | DomainResult.Succeeded(Some _) -> return true
            | _ -> return false
        }

    let private reserveName
        (documents: IStateDocumentStore)
        (name: LoginName)
        (account: AccountId)
        (cancellationToken: CancellationToken)
        : Task<DomainResult<unit, LoginFailure>> =
        task {
            let index =
                { SchemaVersion = schemaVersion
                  NormalizedName = name.Normalized
                  Account = account.Value }

            let! write =
                documents.Create(
                    nameKey name,
                    JsonSerializer.Serialize(index, json),
                    cancellationToken
                )

            match write with
            | :? DocumentWriteResult.Written -> return DomainResult.Succeeded()
            | _ -> return DomainResult.Failed LoginFailure.NameTaken
        }

    let private loginDocument
        (account: AccountId)
        (name: LoginName)
        (password: string)
        (now: DateTimeOffset)
        =
        { SchemaVersion = schemaVersion
          Account = account.Value
          Name = name.Value
          NormalizedName = name.Normalized
          PasswordHash = Passwords.hash password
          SetAt = now }

    /// Gives the account, which has no login yet, this name and password. The name is reserved
    /// first, so a taken name refuses before anything else is written.
    let register
        (documents: IStateDocumentStore)
        (account: AccountId)
        (name: LoginName)
        (password: string)
        (now: DateTimeOffset)
        (cancellationToken: CancellationToken)
        : Task<DomainResult<LoginDocument, LoginFailure>> =
        task {
            match Passwords.validate password with
            | DomainResult.Failed failure ->
                return DomainResult.Failed(LoginFailure.Password failure)
            | DomainResult.Succeeded password ->
                let! existing = forAccount documents account cancellationToken

                match existing with
                | DomainResult.Failed failure -> return DomainResult.Failed failure
                | DomainResult.Succeeded(Some _) ->
                    return DomainResult.Failed LoginFailure.AlreadyNamed
                | DomainResult.Succeeded None ->
                    let! reserved = reserveName documents name account cancellationToken

                    match reserved with
                    | DomainResult.Failed failure -> return DomainResult.Failed failure
                    | DomainResult.Succeeded() ->
                        let document = loginDocument account name password now

                        let! write =
                            documents.Create(
                                key account,
                                JsonSerializer.Serialize(document, json),
                                cancellationToken
                            )

                        match write with
                        | :? DocumentWriteResult.Written -> return DomainResult.Succeeded document
                        | _ -> return DomainResult.Failed LoginFailure.Conflict
        }

    /// A hash to verify against when the name names no account, so a request for a name that
    /// does not exist takes the time one that does would.
    let private absent = lazy (Passwords.hash (Guid.NewGuid().ToString "N"))

    /// The account this name and password sign in to, or Refused. The name is looked up through
    /// the index and the password checked against the login's hash; every refusal takes the time
    /// a check does, and none says which part was wrong.
    let verify
        (documents: IStateDocumentStore)
        (name: string | null)
        (password: string | null)
        (cancellationToken: CancellationToken)
        : Task<DomainResult<AccountId * LoginDocument, LoginFailure>> =
        task {
            let presented =
                match password with
                | null -> ""
                | text -> text

            match LoginName.Create name with
            | DomainResult.Failed _ ->
                Passwords.verify absent.Value presented |> ignore
                return DomainResult.Failed LoginFailure.Refused
            | DomainResult.Succeeded name ->
                let! index = documents.Read(nameKey name, cancellationToken)

                let account =
                    match index with
                    | null -> None
                    | document ->
                        match parseName document with
                        | Some value ->
                            match AccountId.Create value.Account with
                            | DomainResult.Succeeded account -> Some account
                            | DomainResult.Failed _ -> None
                        | None -> None

                match account with
                | None ->
                    Passwords.verify absent.Value presented |> ignore
                    return DomainResult.Failed LoginFailure.Refused
                | Some account ->
                    let! loaded = forAccount documents account cancellationToken

                    match loaded with
                    | DomainResult.Succeeded(Some login) when
                        Passwords.verify login.Document.PasswordHash presented
                        ->
                        return DomainResult.Succeeded(account, login.Document)
                    | DomainResult.Succeeded(Some _) ->
                        return DomainResult.Failed LoginFailure.Refused
                    | DomainResult.Succeeded None ->
                        Passwords.verify absent.Value presented |> ignore
                        return DomainResult.Failed LoginFailure.Refused
                    | DomainResult.Failed failure -> return DomainResult.Failed failure
        }

    /// Sets the account's password: with a name when the account has no login yet, which then
    /// registers one; without a name, or with its own, when it has, which replaces the hash
    /// against the revision the login was read at.
    let set
        (documents: IStateDocumentStore)
        (account: AccountId)
        (name: string | null)
        (password: string)
        (now: DateTimeOffset)
        (cancellationToken: CancellationToken)
        : Task<DomainResult<LoginDocument, LoginFailure>> =
        task {
            match Passwords.validate password with
            | DomainResult.Failed failure ->
                return DomainResult.Failed(LoginFailure.Password failure)
            | DomainResult.Succeeded password ->
                let! existing = forAccount documents account cancellationToken

                match existing with
                | DomainResult.Failed failure -> return DomainResult.Failed failure
                | DomainResult.Succeeded None ->
                    match name with
                    | null -> return DomainResult.Failed LoginFailure.NameRequired
                    | text when String.IsNullOrWhiteSpace text ->
                        return DomainResult.Failed LoginFailure.NameRequired
                    | text ->
                        match LoginName.Create text with
                        | DomainResult.Failed failure ->
                            return DomainResult.Failed(LoginFailure.Name failure)
                        | DomainResult.Succeeded name ->
                            return! register documents account name password now cancellationToken
                | DomainResult.Succeeded(Some loaded) ->
                    let sameName =
                        match name with
                        | null -> true
                        | text when String.IsNullOrWhiteSpace text -> true
                        | text ->
                            String.Equals(
                                text.Trim().ToLowerInvariant(),
                                loaded.Document.NormalizedName,
                                StringComparison.Ordinal
                            )

                    if not sameName then
                        return DomainResult.Failed LoginFailure.AlreadyNamed
                    else
                        let document =
                            { loaded.Document with
                                PasswordHash = Passwords.hash password
                                SetAt = now }

                        let! write =
                            documents.Update(
                                key account,
                                loaded.Revision,
                                JsonSerializer.Serialize(document, json),
                                cancellationToken
                            )

                        match write with
                        | :? DocumentWriteResult.Written -> return DomainResult.Succeeded document
                        | _ -> return DomainResult.Failed LoginFailure.Conflict
        }

    /// The refusal a locked-out caller receives before any password is looked at.
    let locked () =
        ApiError("login.locked", "Too many attempts. Try again in fifteen minutes.")

    let toError (failure: LoginFailure) : ApiError =
        match failure with
        | LoginFailure.Name LoginNameFailure.Required ->
            ApiError("login.name", "Enter a player name.")
        | LoginFailure.Name LoginNameFailure.TooShort ->
            ApiError(
                "login.name",
                $"A player name is at least {LoginName.MinimumLength} characters."
            )
        | LoginFailure.Name LoginNameFailure.TooLong ->
            ApiError(
                "login.name",
                $"A player name is at most {LoginName.MaximumLength} characters."
            )
        | LoginFailure.Name _ ->
            ApiError(
                "login.name",
                "A player name is letters, digits, dots, hyphens and underscores only."
            )
        | LoginFailure.Password PasswordFailure.Required ->
            ApiError("login.password", "Enter a password.")
        | LoginFailure.Password PasswordFailure.TooShort ->
            ApiError(
                "login.password",
                $"A password is at least {Passwords.MinimumLength} characters."
            )
        | LoginFailure.Password _ ->
            ApiError(
                "login.password",
                $"A password is at most {Passwords.MaximumLength} characters."
            )
        | LoginFailure.NameTaken -> ApiError("login.taken", "That player name is taken.")
        | LoginFailure.AlreadyNamed ->
            ApiError("login.named", "Your account already has a player name.")
        | LoginFailure.NameRequired ->
            ApiError("login.name", "Choose a player name to sign in with.")
        | LoginFailure.Refused ->
            ApiError("login.refused", "That player name and password do not match.")
        | LoginFailure.Conflict ->
            ApiError("login.conflict", "Your login changed underneath this request. Try again.")
        | LoginFailure.Damaged ->
            ApiError("login.damaged", "A login record could not be read. Nothing changed.")
