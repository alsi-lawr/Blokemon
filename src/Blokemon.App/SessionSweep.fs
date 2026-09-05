namespace Blokemon.App

open System
open System.Threading
open System.Threading.Tasks
open Blokemon.App.Contracts

/// Removes session documents past their absolute expiry. A session is refused the moment it
/// expires whether or not the sweep has run; the sweep keeps the store from holding the dead.
module SessionSweep =

    /// Deletes every expired session document and returns how many it removed.
    let run
        (documents: IStateDocumentStore)
        (listing: IDocumentListing)
        (now: DateTimeOffset)
        (cancellationToken: CancellationToken)
        : Task<int> =
        task {
            let! sessions = listing.List("session/", cancellationToken)
            let mutable removed = 0

            for summary in sessions do
                match summary.Projection with
                | :? DocumentProjection.Expiry as expiry when
                    expiry.ExpiresAt.HasValue && expiry.ExpiresAt.Value <= now
                    ->
                    do! documents.Delete(summary.Key, cancellationToken)
                    removed <- removed + 1
                | _ -> ()

            return removed
        }
