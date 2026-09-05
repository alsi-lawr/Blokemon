namespace Blokemon.App

open System
open System.Net
open System.Net.Sockets
open Microsoft.Extensions.Configuration

/// The `Blokemon:Hosting` contract, validated once at start-up. Forwarded client addresses are
/// applied only when the connection comes from one of the known proxies (BLOKEMON-D-045); with
/// none listed the host trusts no forwarded header and every caller is its connection's address.
type HostingConfiguration =
    { KnownProxies: IPNetwork array }

    member this.TrustsForwardedHeaders = this.KnownProxies.Length > 0

/// Reads and validates the `Blokemon:Hosting` contract; an invalid value fails start-up with a
/// message naming the key, like the identity settings.
module HostingConfiguration =

    [<Literal>]
    let KnownProxiesKey = "Blokemon:Hosting:KnownProxies"

    let private invalid (message: string) =
        raise (InvalidOperationException message)

    /// One entry: an IP address, or a CIDR range such as `10.0.0.0/8` or `fd00::/8`. A bare
    /// address is the range of that one address. The dotted or coloned form is required so a
    /// stray number is not read as an address.
    let private network (key: string) (text: string) : IPNetwork =
        let trimmed = text.Trim()

        match IPNetwork.TryParse trimmed with
        | true, range -> range
        | _ ->
            match IPAddress.TryParse trimmed with
            | true, NonNull address when
                address.AddressFamily = AddressFamily.InterNetworkV6 && trimmed.Contains ':'
                ->
                IPNetwork(address, 128)
            | true, NonNull address when
                address.AddressFamily = AddressFamily.InterNetwork
                && trimmed.Split('.').Length = 4
                ->
                IPNetwork(address, 32)
            | _ -> invalid $"{key} must be an IP address or a CIDR range such as 10.0.0.0/8."

    let Resolve (configuration: IConfiguration) : HostingConfiguration =
        ArgumentNullException.ThrowIfNull(configuration, nameof configuration)

        let proxies =
            configuration.GetSection(KnownProxiesKey).GetChildren()
            |> Seq.map (fun child ->
                match child.Value with
                | null ->
                    invalid
                        $"{child.Path} must be an IP address or a CIDR range such as 10.0.0.0/8."
                | text when String.IsNullOrWhiteSpace text ->
                    invalid
                        $"{child.Path} must be an IP address or a CIDR range such as 10.0.0.0/8."
                | text -> network child.Path text)
            |> Seq.toArray

        { KnownProxies = proxies }
