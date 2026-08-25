namespace Blokemon.Game

open System.Collections.Generic
open System.Collections.Immutable
open Blokemon.Core.SetDesign
open Blokemon.Game.EffectTargeting
open Blokemon.Game.EffectSelection
open Blokemon.Game.EffectPredicates

/// Whether activating an effect could do anything at all.
///
/// An activated party trick or local house rule is only worth offering when its program, read
/// against the table as it stands, still has one instruction in it that would change something.
/// Two things can empty a program out: the conditions guarding the branch that does the work, and
/// the work itself having nothing left to do it to. Both are asked here, and the conditions are
/// asked with the execution path's own predicate evaluation, so the answer the gate gets is the
/// answer the run would give.
module internal EffectViability =

    /// The staging area an activation would run against: read throughout, never written. No
    /// choices have been made yet, which is what makes this a question about the program rather
    /// than about one way of answering it.
    let private probe
        (catalog: AuthorityCatalog)
        (builder: MatchBuilder)
        (actor: PlayerId)
        (source: CardState)
        (effect: EffectId)
        (isHouseRule: bool)
        =
        EffectRuntime(
            builder,
            catalog,
            actor,
            source,
            effect,
            ImmutableArray<EffectChoice>.Empty,
            false,
            isHouseRule,
            HashSet<EffectId>(),
            ImmutableArray<bool>.Empty,
            ValueNone,
            ResolutionTrace.none
        )

    let private candidates
        (catalog: AuthorityCatalog)
        (runtime: EffectRuntime)
        (instruction: BlokemonEffectInstruction)
        =
        resolveCandidates catalog runtime.Builder runtime.Actor runtime.Source instruction ValueNone

    /// How many cards a draw would take. A draw counted off the beer mats cannot be known before
    /// they are tossed, so it is read at its printed size and left to the run.
    let private drawCount (runtime: EffectRuntime) (instruction: BlokemonEffectInstruction) =
        if instruction.Selection = BlokemonSelection.UntilBlankSide then
            instruction.Amount
        elif instruction.ValueSource = BlokemonValueSource.MittCardsNeeded then
            resolveValue runtime instruction
        else
            instruction.Amount

    /// What one instruction would change, by opcode, derived from the census of every Activated
    /// trick and activated house rule the authority prints. An opcode this does not name is taken
    /// to do something: the gate only ever withdraws an activation it can show is dead.
    let private instructionActs
        (catalog: AuthorityCatalog)
        (runtime: EffectRuntime)
        (instruction: BlokemonEffectInstruction)
        =
        let cards () = candidates catalog runtime instruction
        let anyCandidate () = cards () |> Seq.isEmpty |> not

        match instruction.Opcode with
        // Spending the round's one go is bookkeeping and not a move in itself; whether the go has
        // already been spent is asked once, of the whole activation, below.
        | BlokemonOpcode.OncePerRound -> false
        | BlokemonOpcode.HealDamage ->
            instruction.Amount > 0 && cards () |> Seq.exists (fun card -> card.Damage > 0)
        | BlokemonOpcode.PlaceDamageCounters -> instruction.Amount > 0 && anyCandidate ()
        | BlokemonOpcode.DrawFromStack ->
            drawCount runtime instruction > 0
            && runtime.Builder.CardsIn(runtime.Actor, CardZone.Stack) |> Seq.isEmpty |> not
        // Shuffling is something a program does on its way past, never the reason to activate
        // one: a search that shuffles and finds nothing has still found nothing.
        | BlokemonOpcode.ShuffleStack -> false
        | BlokemonOpcode.SearchStack
        | BlokemonOpcode.TransformFromStack
        | BlokemonOpcode.ChuckVim
        | BlokemonOpcode.ChuckCards -> anyCandidate ()
        // Once a selection is running these two work on it rather than on a set of their own, so
        // by themselves they add nothing: showing an empty selection shows nothing, and moving
        // one moves nothing. What the selection cost is answered where it was made.
        | BlokemonOpcode.RevealCards -> not runtime.HasCardSelection && anyCandidate ()
        | BlokemonOpcode.MoveCards ->
            instruction.Amount > 0
            && (hasDeclaredSources instruction || not runtime.HasCardSelection)
            && anyCandidate ()
        // A house rule's own card is not its to throw away, and the run skips it.
        | BlokemonOpcode.ChuckSelf -> not runtime.IsHouseRule
        | _ -> true

    /// Whether a conditional would take the branch that does the work. The Optional predicate is
    /// the player's own answer and never a gate: it is the difference between a trick that may be
    /// declined and one that could not have worked whatever the player said.
    let private conditionHolds
        (catalog: AuthorityCatalog)
        (runtime: EffectRuntime)
        (instruction: BlokemonEffectInstruction)
        (path: string)
        =
        instruction.Predicates
        |> Array.filter (fun predicate -> predicate.Condition <> BlokemonCondition.Optional)
        |> Array.forall (fun predicate -> evaluatePredicate catalog runtime predicate path)

    /// The instructions that leave a running card selection behind them, exactly as the executor
    /// records it: whatever follows one of these works on what it selected.
    let private recordSelection (runtime: EffectRuntime) (instruction: BlokemonEffectInstruction) =
        match instruction.Opcode with
        | BlokemonOpcode.SearchStack
        | BlokemonOpcode.MoveCards
        | BlokemonOpcode.RevealCards -> runtime.HasCardSelection <- true
        | _ -> ()

    /// One pass along a branch, in the order the run would take it, stopping at the first
    /// instruction that would change something. A conditional is followed down the one branch its
    /// conditions choose; every other branch a program carries is read as reachable.
    let rec private programActs
        (catalog: AuthorityCatalog)
        (runtime: EffectRuntime)
        (program: BlokemonEffectInstruction array)
        (parentPath: string)
        =
        let mutable acts = false
        let mutable index = 0

        while not acts && index < program.Length do
            let instruction = program[index]
            let path = $"{parentPath}/{index}"

            acts <-
                match instruction.Opcode with
                | BlokemonOpcode.Conditional ->
                    if conditionHolds catalog runtime instruction path then
                        programActs catalog runtime instruction.Then (path + "/then")
                    else
                        programActs catalog runtime instruction.Otherwise (path + "/otherwise")
                | _ ->
                    instructionActs catalog runtime instruction
                    || programActs catalog runtime instruction.Then (path + "/then")
                    || programActs catalog runtime instruction.Otherwise (path + "/otherwise")

            recordSelection runtime instruction
            index <- index + 1

        acts

    /// Whether activating this effect from this card, against this table, could change anything.
    let activationCanAct
        (catalog: AuthorityCatalog)
        (state: MatchState)
        (actor: PlayerId)
        (source: CardState)
        (effect: EffectId)
        (program: BlokemonEffectInstruction array)
        (isHouseRule: bool)
        =
        let builder = MatchBuilder(state, catalog)

        // The one go a round allows is spent for good: the handler refuses a second activation on
        // exactly this test, so the table stops offering one at exactly the same moment.
        not (Seq.contains effect builder.RoundUsage.EffectsUsed)
        && programActs
            catalog
            (probe catalog builder actor source effect isHouseRule)
            program
            "root"
