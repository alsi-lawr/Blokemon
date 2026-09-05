namespace Blokemon.App

/// The session the request's bearer token established, set by the host's session middleware
/// before any endpoint runs and read by every endpoint that needs one. Null on an anonymous
/// route with no valid session.
[<Sealed>]
type CurrentSession() =
    member val Session: Session | null = null with get, set
