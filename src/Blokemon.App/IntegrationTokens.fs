namespace Blokemon.App

open System
open System.Buffers.Text
open System.Security.Cryptography
open System.Text
open Blokemon.Product

/// A tenant's integration token: `blkm_{tenantId}_{secret}`. The tenant id travels in clear so
/// the record is found without a scan; the secret is 256 bits from the cryptographic generator,
/// shown once, and only its hash is stored. Comparison takes the same time whatever is presented.
module IntegrationTokens =

    [<Literal>]
    let Prefix = "blkm_"

    /// The entropy of the secret part, in bytes.
    let SecretBytes = 32

    let hash (secret: string) =
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes secret))

    /// A fresh token for the tenant and the verifier that recognises it.
    let mint (tenant: TenantId) (now: DateTimeOffset) : string * IntegrationTokenVerifier =
        let secret = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes SecretBytes)
        $"{Prefix}{tenant}_{secret}", { Hash = hash secret; IssuedAt = now }

    /// The tenant a presented token names and its secret part, or None for anything that is not
    /// shaped like a token.
    let parse (token: string | null) : (TenantId * string) option =
        match token with
        | null -> None
        | text when not (text.StartsWith(Prefix, StringComparison.Ordinal)) -> None
        | text ->
            match text.Substring(Prefix.Length).Split('_', 2) with
            | [| id; secret |] when secret.Length > 0 ->
                match TenantId.Create id with
                | DomainResult.Succeeded tenant -> Some(tenant, secret)
                | DomainResult.Failed _ -> None
            | _ -> None

    /// Whether the secret is the one the verifier was minted with, in fixed time; a tenant
    /// without a verifier recognises nothing.
    let matches (verifier: IntegrationTokenVerifier | null) (secret: string) =
        match verifier with
        | null -> false
        | stored ->
            CryptographicOperations.FixedTimeEquals(
                ReadOnlySpan<byte>(Encoding.UTF8.GetBytes stored.Hash),
                ReadOnlySpan<byte>(Encoding.UTF8.GetBytes(hash secret))
            )
