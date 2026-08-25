namespace Blokemon.Core.SetDesign

open System.Collections.Generic

type BlokemonValidationIssue = { Code: string; Message: string }

type BlokemonValidationResult =
    { Issues: BlokemonValidationIssue array }

    member this.IsValid = this.Issues.Length = 0

module internal BlokemonValidation =

    let check
        (condition: bool)
        (code: string)
        (message: string)
        (issues: ResizeArray<BlokemonValidationIssue>)
        =
        if not condition then
            issues.Add({ Code = code; Message = message })
