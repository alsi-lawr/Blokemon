namespace Blokemon.App.Tests

open System
open Blokemon.App
open Blokemon.App.Contracts
open Blokemon.Product
open FsUnit
open TUnit.Core

type LoginTests() =

    let name (text: string) =
        match LoginName.Create text with
        | DomainResult.Succeeded name -> name
        | DomainResult.Failed failure -> failwith $"Bad login name: {failure}"

    let register (documents: MemoryDocumentStore) account text password =
        Logins.register documents account (name text) password now Unchecked.defaultof<_>

    let verify (documents: MemoryDocumentStore) text password =
        Logins.verify documents text password Unchecked.defaultof<_>

    [<Test>]
    member _.``registering a login should reserve the name and be found by name and password``() =
        task {
            let documents = MemoryDocumentStore()
            let account = AccountId.Mint()

            let! registered = register documents account "Alex_1" "correct horse"
            let! found = verify documents "alex_1" "correct horse"
            let! loaded = Logins.forAccount documents account Unchecked.defaultof<_>

            (succeeded registered).Name |> should equal "Alex_1"
            (succeeded registered).NormalizedName |> should equal "alex_1"
            (succeeded registered).PasswordHash |> should not' (contain "correct horse")
            (succeeded registered).PasswordHash |> should startWith "pbkdf2-sha512$210000$"
            fst (succeeded found) |> should equal account
            (Option.get (succeeded loaded)).Document.Account |> should equal account.Value

            keysUnder documents "login"
            |> should equal (List.sort [ Logins.key account; Logins.nameKey (name "ALEX_1") ])
        }

    [<Test>]
    member _.``a name already taken in any case should be refused and nothing written``() =
        task {
            let documents = MemoryDocumentStore()
            let! _ = register documents (AccountId.Mint()) "Alex" "correct horse"
            let before = documents.Snapshot

            let! refused = register documents (AccountId.Mint()) "ALEX" "another one"

            failed refused |> should equal LoginFailure.NameTaken
            documents.Snapshot |> should equal before
        }

    [<Test>]
    member _.``a wrong password or an unknown name should be refused alike and nothing written``() =
        task {
            let documents = MemoryDocumentStore()
            let! _ = register documents (AccountId.Mint()) "Alex" "correct horse"
            let before = documents.Snapshot

            let! wrongPassword = verify documents "Alex" "wrong horse"
            let! unknownName = verify documents "Nobody" "correct horse"
            let! malformedName = verify documents "a" "correct horse"
            let! noPassword = verify documents "Alex" null

            failed wrongPassword |> should equal LoginFailure.Refused
            failed unknownName |> should equal LoginFailure.Refused
            failed malformedName |> should equal LoginFailure.Refused
            failed noPassword |> should equal LoginFailure.Refused
            documents.Snapshot |> should equal before
        }

    [<Test>]
    member _.``a password outside the bounds should be refused before anything is written``() =
        task {
            let documents = MemoryDocumentStore()
            let account = AccountId.Mint()

            let! short = register documents account "Alex" "seven77"
            let! long = register documents account "Alex" (String('x', 129))
            let! empty = register documents account "Alex" ""

            failed short |> should equal (LoginFailure.Password PasswordFailure.TooShort)
            failed long |> should equal (LoginFailure.Password PasswordFailure.TooLong)
            failed empty |> should equal (LoginFailure.Password PasswordFailure.Required)
            documents.Keys |> should be Empty
        }

    [<Test>]
    member _.``setting a password on an unnamed account should need a name and then register it``
        ()
        =
        task {
            let documents = MemoryDocumentStore()
            let account = AccountId.Mint()

            let! unnamed =
                Logins.set documents account null "correct horse" now Unchecked.defaultof<_>

            let! badName =
                Logins.set documents account "no spaces" "correct horse" now Unchecked.defaultof<_>

            let! named =
                Logins.set documents account "Alex" "correct horse" now Unchecked.defaultof<_>

            let! found = verify documents "alex" "correct horse"

            failed unnamed |> should equal LoginFailure.NameRequired
            failed badName |> should equal (LoginFailure.Name LoginNameFailure.Malformed)
            (succeeded named).Name |> should equal "Alex"
            fst (succeeded found) |> should equal account
        }

    [<Test>]
    member _.``changing a password should keep the name, replace the hash and refuse another name``
        ()
        =
        task {
            let documents = MemoryDocumentStore()
            let account = AccountId.Mint()
            let! registered = register documents account "Alex" "correct horse"
            let later = now.AddHours 1.0

            let! changed =
                Logins.set documents account null "battery staple" later Unchecked.defaultof<_>

            let! sameName =
                Logins.set documents account " alex " "battery staple" later Unchecked.defaultof<_>

            let! renamed =
                Logins.set documents account "Someone" "battery staple" later Unchecked.defaultof<_>

            let! old = verify documents "Alex" "correct horse"
            let! fresh = verify documents "Alex" "battery staple"

            (succeeded changed).Name |> should equal "Alex"
            (succeeded changed).SetAt |> should equal later

            (succeeded changed).PasswordHash
            |> should not' (equal (succeeded registered).PasswordHash)

            (succeeded sameName).Name |> should equal "Alex"
            failed renamed |> should equal LoginFailure.AlreadyNamed
            failed old |> should equal LoginFailure.Refused
            fst (succeeded fresh) |> should equal account
            keysUnder documents "loginname/" |> List.length |> should equal 1
        }

    [<Test>]
    member _.``a second login on a named account should be refused``() =
        task {
            let documents = MemoryDocumentStore()
            let account = AccountId.Mint()
            let! _ = register documents account "Alex" "correct horse"

            let! again = register documents account "Other" "correct horse"

            failed again |> should equal LoginFailure.AlreadyNamed
            keysUnder documents "loginname/" |> List.length |> should equal 1
        }

    [<Test>]
    member _.``a password hash should verify its own password only and carry its iteration count``
        ()
        =
        let stored = Passwords.hash "correct horse"

        Passwords.verify stored "correct horse" |> should be True
        Passwords.verify stored "Correct horse" |> should be False
        Passwords.verify "not-a-hash" "correct horse" |> should be False
        Passwords.verify "pbkdf2-sha512$0$AA==$AA==" "correct horse" |> should be False
        Passwords.hash "correct horse" |> should not' (equal stored)
        (stored.Split '$').Length |> should equal 4

    [<Test>]
    member _.``every login key should be fixed literals and hexadecimal within the widened bound``
        ()
        =
        let account = AccountId.Mint()
        (Logins.key account).Length |> should equal 42
        (Logins.nameKey (name "Alex")).Length |> should equal 74
        Logins.nameKey (name "Alex") |> should equal (Logins.nameKey (name "aLEX"))

        (Logins.nameKey (name "Alex")).Substring("loginname/".Length)
        |> Seq.forall Uri.IsHexDigit
        |> should be True
