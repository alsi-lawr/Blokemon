namespace Blokemon.App

open Blokemon.Product

/// Whose documents an application instance acts on. The browser-local host is anonymous and
/// keeps the literal keys it has always used; the server host acts for one account in one
/// tenant, the account for the profile and the tenant for the edition and owner authority.
[<RequireQualifiedAccess>]
type ApplicationPrincipal =
    | BrowserLocal
    | Account of Account: AccountId * Tenant: TenantId

/// The keys of the three documents a player owns: the profile, the saved battle and the battle
/// history. Purging a player deletes exactly these.
type PlayerDocumentKeys =
    { Profile: string
      Match: string
      MatchHistory: string }

module PlayerDocumentKeys =

    /// The browser-local host's literal keys, unchanged.
    let browserLocal =
        { Profile = "profile"
          Match = "match"
          MatchHistory = "match-history" }

    /// Where one account's three documents live on the server. No provider subject ever
    /// appears here: the account is a Blokemon-minted identity.
    let forAccount (account: AccountId) =
        { Profile = $"a/{account}/profile"
          Match = $"a/{account}/match"
          MatchHistory = $"a/{account}/match-history" }

    let ofPrincipal (principal: ApplicationPrincipal) =
        match principal with
        | ApplicationPrincipal.BrowserLocal -> browserLocal
        | ApplicationPrincipal.Account(account, _) -> forAccount account
