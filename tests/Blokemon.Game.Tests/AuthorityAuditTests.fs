namespace Blokemon.Game.Tests

open System
open System.IO
open System.Text.Json
open Blokemon.Core.SetDesign
open Blokemon.Game
open FsUnit
open TUnit.Core

[<AutoOpen>]
module private AuthorityAuditFixtures =

    let seedForBadge () =
        let rec search (seed: uint64) =
            if seed >= 100UL then 0UL
            elif BlokemonSeededRandom(seed).NextInt 2 = 1 then seed
            else search (seed + 1UL)

        search 0UL

    let rec mutateInstructions
        (program: BlokemonEffectInstruction array)
        (mutation: BlokemonEffectInstruction -> BlokemonEffectInstruction)
        =
        program
        |> Array.map (fun instruction ->
            let changed = mutation instruction

            { changed with
                Then = mutateInstructions changed.Then mutation
                Otherwise = mutateInstructions changed.Otherwise mutation })

    let mutateReactiveProgram
        (program: BlokemonEffectInstruction array)
        (trigger: BlokemonTrigger)
        =
        mutateInstructions program (fun instruction ->
            match trigger, instruction.Opcode with
            | BlokemonTrigger.OnOwnBlokeSentHomeByOtherAttackDamage, BlokemonOpcode.MoveVim ->
                { instruction with
                    MechanicalTypes = Array.empty }
            | BlokemonTrigger.BeforeSelfSentHomeByAttackDamage, BlokemonOpcode.RecoverFromSendHome ->
                { instruction with Amount = 20 }
            | BlokemonTrigger.AfterSelfDamagedByAttack, BlokemonOpcode.PlaceDamageCounters ->
                { instruction with Amount = 4 }
            | BlokemonTrigger.AfterSelfSentHomeByAttackDamage, BlokemonOpcode.SendHome ->
                { instruction with
                    Opcode = BlokemonOpcode.PlaceDamageCounters
                    Amount = 4 }
            | BlokemonTrigger.OnBarChitTaken, BlokemonOpcode.TakeExtraBarChit ->
                { instruction with Amount = 2 }
            | _ -> instruction)

    let private observeKnockoutVimMove (engine: MatchEngine) =
        let state =
            MatchScenario.BattleState
                "BLK-003"
                "BLK-001"
                [ "VIM-BLAZED"; "VIM-BLAZED"; "VIM-SOBER" ]
                103UL

        let triggerSource =
            MatchScenario.PlainCard
                "trigger-source"
                "BLK-026"
                MatchScenario.SecondPlayer
                CardZone.Booth
                -1

        let movableVim =
            MatchScenario.AttachedCard
                "movable-vim"
                "VIM-SOBER"
                MatchScenario.SecondPlayer
                CardZone.Attached
                -1
                (CardInstanceId "defender")

        let prize =
            MatchScenario.PlainCard "prize" "VIM-LAIRY" MatchScenario.FirstPlayer CardZone.BarChit 0

        let defender =
            { state.Card(CardInstanceId "defender") with
                Attachments = FrozenList<CardInstanceId>.Create movableVim.Id }

        let state =
            MatchScenario.WithCards state [ defender; triggerSource; movableVim; prize ]

        let state = MatchScenario.WithBarChits state MatchScenario.FirstPlayer 1

        let attacked =
            MatchScenario.Applied(
                engine.Apply(state, MatchScenario.AttackCommand state "BLK-003-B01")
            )

        if attacked.PendingKnockout.IsNone then
            0
        else
            let resolved =
                MatchScenario.Applied(
                    engine.Apply(
                        attacked,
                        MatchScenario.Command
                            attacked
                            "resolve-mutated-knockout-trigger"
                            MatchScenario.SecondPlayer
                            FrozenList.empty
                            (MatchAction.ResolveKnockoutTrigger(ValueSome movableVim.Id))
                    )
                )

            if (resolved.Card movableVim.Id).AttachedTo = ValueSome triggerSource.Id then
                1
            else
                0

    let private observeRecovery (engine: MatchEngine) =
        let state =
            MatchScenario.BattleState
                "BLK-076"
                "BLK-068"
                [ "VIM-LAIRY"; "VIM-SOBER"; "VIM-SOBER" ]
                0UL

        let applied =
            MatchScenario.Applied(
                engine.Apply(state, MatchScenario.AttackCommand state "BLK-076-B02")
            )

        (applied.Card(CardInstanceId "defender")).Damage

    let private observeDamageRetaliation (engine: MatchEngine) =
        let state = MatchScenario.BattleState "BLK-076" "BLK-107" [ "VIM-LAIRY" ] 107UL

        let applied =
            MatchScenario.Applied(
                engine.Apply(state, MatchScenario.AttackCommand state "BLK-076-B01")
            )

        (applied.Card(CardInstanceId "attacker")).Damage

    let private observeSendHomeRetaliation (engine: MatchEngine) =
        let state =
            MatchScenario.BattleState
                "BLK-076"
                "BLK-110"
                [ "VIM-LAIRY"; "VIM-SOBER"; "VIM-SOBER" ]
                (seedForBadge ())

        let applied =
            MatchScenario.Applied(
                engine.Apply(state, MatchScenario.AttackCommand state "BLK-076-B02")
            )

        (applied.Card(CardInstanceId "attacker")).Damage

    let private observeBarChitTrigger (engine: MatchEngine) =
        let state =
            MatchScenario.BattleState
                "BLK-003"
                "BLK-001"
                [ "VIM-BLAZED"; "VIM-BLAZED"; "VIM-SOBER" ]
                0UL

        let triggeredPrize =
            MatchScenario.PlainCard
                "triggered-prize"
                "BLK-113"
                MatchScenario.FirstPlayer
                CardZone.BarChit
                0

        let extraPrizes =
            [ MatchScenario.PlainCard
                  "extra-prize-1"
                  "VIM-LAIRY"
                  MatchScenario.FirstPlayer
                  CardZone.BarChit
                  1
              MatchScenario.PlainCard
                  "extra-prize-2"
                  "VIM-SOBER"
                  MatchScenario.FirstPlayer
                  CardZone.BarChit
                  2 ]

        let defenderBench =
            MatchScenario.PlainCard
                "defender-bench"
                "BLK-004"
                MatchScenario.SecondPlayer
                CardZone.Booth
                -1

        let state =
            MatchScenario.WithCards
                state
                (List.concat [ [ triggeredPrize ]; extraPrizes; [ defenderBench ] ])

        let state = MatchScenario.WithBarChits state MatchScenario.FirstPlayer 3

        let attacked =
            MatchScenario.Applied(
                engine.Apply(state, MatchScenario.AttackCommand state "BLK-003-B01")
            )

        let resolved =
            MatchScenario.Applied(
                engine.Apply(
                    attacked,
                    MatchScenario.Command
                        attacked
                        "resolve-mutated-bar-chit-trigger"
                        MatchScenario.FirstPlayer
                        FrozenList.empty
                        (MatchAction.ResolveBarChitTrigger true)
                )
            )

        (resolved.Player MatchScenario.FirstPlayer).BarChitsRemaining

    let observeReactiveTrigger (engine: MatchEngine) (trigger: BlokemonTrigger) =
        match trigger with
        | BlokemonTrigger.OnOwnBlokeSentHomeByOtherAttackDamage -> observeKnockoutVimMove engine
        | BlokemonTrigger.BeforeSelfSentHomeByAttackDamage -> observeRecovery engine
        | BlokemonTrigger.AfterSelfDamagedByAttack -> observeDamageRetaliation engine
        | BlokemonTrigger.AfterSelfSentHomeByAttackDamage -> observeSendHomeRetaliation engine
        | BlokemonTrigger.OnBarChitTaken -> observeBarChitTrigger engine
        | other ->
            raise (ArgumentOutOfRangeException(nameof trigger, $"Unhandled trigger {other}."))

type AuthorityAuditTests() =

    [<Test>]
    [<Arguments("BLK-026", "BLK-026-T01", BlokemonTrigger.OnOwnBlokeSentHomeByOtherAttackDamage, 1)>]
    [<Arguments("BLK-068", "BLK-068-T01", BlokemonTrigger.BeforeSelfSentHomeByAttackDamage, 160)>]
    [<Arguments("BLK-107", "BLK-107-T01", BlokemonTrigger.AfterSelfDamagedByAttack, 40)>]
    [<Arguments("BLK-110", "BLK-110-T01", BlokemonTrigger.AfterSelfSentHomeByAttackDamage, 40)>]
    [<Arguments("BLK-113", "BLK-113-T01", BlokemonTrigger.OnBarChitTaken, 0)>]
    member _.``mutating a declared reactive trigger program should change what the runtime does``
        (cardId: string, effectId: string, trigger: BlokemonTrigger, expectedObservation: int)
        =
        let owner =
            MatchScenario.Authority.Collectibles
            |> Array.find (fun card -> card.Id = cardId)

        let trick =
            owner.PartyTricks |> Array.find (fun value -> value.MechanicalId = effectId)

        let changedTrigger =
            { trick with
                Program = mutateReactiveProgram trick.Program trigger }

        let changedOwner =
            { owner with
                PartyTricks =
                    owner.PartyTricks
                    |> Array.map (fun value ->
                        if value.MechanicalId = changedTrigger.MechanicalId then
                            changedTrigger
                        else
                            value) }

        let authority =
            { MatchScenario.Authority with
                Collectibles =
                    MatchScenario.Authority.Collectibles
                    |> Array.map (fun card ->
                        if card.Id = changedOwner.Id then changedOwner else card) }

        let engine = MatchEngine authority

        let baseline = observeReactiveTrigger (MatchScenario.Engine()) trigger
        let observation = observeReactiveTrigger engine trigger

        observation |> should not' (equal baseline)
        observation |> should equal expectedObservation

    [<Test>]
    member _.``the reconciled 310 effects should flatten to 641 instructions after the fossil gates were removed``
        ()
        =
        use document =
            JsonDocument.Parse(
                File.ReadAllText(
                    Path.Combine(
                        AppContext.BaseDirectory,
                        "Authorities",
                        "sv151-authority-reconciliation.json"
                    )
                )
            )

        let root = document.RootElement
        let reconciled = root.GetProperty("effects").EnumerateArray() |> Seq.toArray

        let effectIds (partyTricks: BlokemonPartyTrick array) attacks houseRules =
            Seq.concat
                [ partyTricks |> Seq.map (fun effect -> effect.MechanicalId)
                  attacks |> Seq.map (fun (effect: BlokemonAttack) -> effect.MechanicalId)
                  houseRules |> Seq.map (fun (effect: BlokemonHouseRule) -> effect.MechanicalId) ]

        let declared =
            Seq.append
                (MatchScenario.Authority.Collectibles
                 |> Seq.collect (fun card ->
                     effectIds card.PartyTricks card.Attacks card.HouseRules))
                (MatchScenario.Authority.Kits
                 |> Seq.collect (fun card ->
                     effectIds card.PartyTricks card.Attacks card.HouseRules))
            |> Seq.sortWith (fun left right -> String.CompareOrdinal(left, right))
            |> Seq.toArray

        let documented =
            reconciled
            |> Array.map (fun effect -> effect.GetProperty("mechanicalId").GetString())
            |> Array.sortWith (fun left right -> String.CompareOrdinal(left, right))

        let audit = BlokemonInterpreter(MatchScenario.Authority).AuditAuthority()

        root.GetProperty("authorityVersion").GetString()
        |> should equal MatchScenario.Authority.ManifestVersion

        documented |> should equal declared
        reconciled.Length |> should equal 310

        reconciled
        |> Array.filter (fun effect ->
            effect.GetProperty("disposition").GetString() = "CorrectedFromCandidate6")
        |> Array.length
        |> should equal 94

        audit.EffectCount |> should equal 310
        // Candidate.6's 643 was derived before BLK-113's SV151-correct optional Booth branch
        // (+1) and before the three fossil Kits lost their spurious Optional wrappers (-3).
        audit.InstructionCount |> should equal 641
        audit.Issues.Count |> should equal 0
