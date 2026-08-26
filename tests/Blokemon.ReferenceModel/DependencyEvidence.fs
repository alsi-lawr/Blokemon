namespace Blokemon.ReferenceModel

open System
open System.Collections.Generic
open System.Reflection

[<RequireQualifiedAccess>]
module DependencyEvidence =

    let directAssemblyReferences () =
        Assembly.GetExecutingAssembly().GetReferencedAssemblies()
        |> Array.choose (fun reference -> reference.Name |> Option.ofObj)
        |> Array.sort

    let transitiveAssemblyReferences () =
        let visited = HashSet<string>(StringComparer.Ordinal)

        let rec visit (assembly: Assembly) =
            for reference in assembly.GetReferencedAssemblies() do
                match reference.Name |> Option.ofObj with
                | None -> ()
                | Some name when visited.Add name ->
                    try
                        reference |> Assembly.Load |> visit
                    with
                    | :? System.IO.FileNotFoundException -> ()
                    | :? System.IO.FileLoadException -> ()
                | Some _ -> ()

        visit (Assembly.GetExecutingAssembly())
        visited |> Seq.sort |> Seq.toArray
