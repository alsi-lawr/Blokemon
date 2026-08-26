namespace Blokemon.ReferenceModel

open System
open System.IO
open System.Text.Json

[<RequireQualifiedAccess>]
type ReferenceCardKind =
    | Bloke = 0
    | Vim = 1
    | Kit = 2

[<RequireQualifiedAccess>]
type ReferenceRank =
    | Regular = 0
    | Seasoned = 1
    | Landlord = 2

[<RequireQualifiedAccess>]
type ReferenceKitKind =
    | BarBit = 0
    | BarKit = 1
    | Mate = 2
    | Local = 3

[<RequireQualifiedAccess>]
type ReferenceMechanicalType =
    | Grass = 0
    | Fire = 1
    | Water = 2
    | Lightning = 3
    | Psychic = 4
    | Fighting = 5
    | Darkness = 6
    | Colorless = 7
    | Dragon = 8
    | Metal = 9

[<RequireQualifiedAccess>]
type ReferenceRoughState =
    | DodgyPint = 0
    | Singed = 1
    | NoddedOff = 2
    | Legless = 3
    | Muddled = 4

[<RequireQualifiedAccess>]
type ReferenceTrigger =
    | Activated = 0
    | AfterSelfDamagedByAttack = 1
    | AfterSelfSentHomeByAttackDamage = 2
    | BeforeSelfSentHomeByAttackDamage = 3
    | Continuous = 4
    | OnBarChitTaken = 5
    | OnOwnBlokeSentHomeByOtherAttackDamage = 6
    | OnPromotionFromMitt = 7

[<RequireQualifiedAccess>]
type ReferenceOpcode =
    | DealPrintedDamage = 0
    | AdjustDamage = 1
    | ScaleDamage = 2
    | DealBoothDamage = 3
    | PlaceDamageCounters = 4
    | DealSelfDamage = 5
    | HealDamage = 6
    | ApplyRoughState = 7
    | ClearRoughState = 8
    | DrawFromStack = 9
    | SearchStack = 10
    | ShuffleStack = 11
    | RevealCards = 12
    | MoveCards = 13
    | ChuckCards = 14
    | AttachVim = 15
    | MoveVim = 16
    | ChuckVim = 17
    | SwapOche = 18
    | PreventDamage = 19
    | PreventEffects = 20
    | ReduceDamage = 21
    | ModifyAttackCost = 22
    | ModifyTaxiFare = 23
    | ModifySoftSpot = 24
    | IgnoreStubbornStreak = 25
    | IgnoreSoftSpotAndStubbornStreak = 26
    | RestrictAttack = 27
    | RestrictTaxi = 28
    | RestrictKit = 29
    | RestrictLocal = 30
    | RestrictEmptiesRecovery = 31
    | ForceBeerMatBlank = 32
    | ReflectAttackDamage = 33
    | BeerMatToss = 34
    | RepeatUntilBlankSide = 35
    | Conditional = 36
    | SendHome = 37
    | RecoverFromSendHome = 38
    | CopyAttack = 39
    | Demote = 40
    | TransformFromStack = 41
    | TakeExtraBarChit = 42
    | PlayAsBloke = 43
    | ChuckSelf = 44
    | TriggeredPartyTrick = 45
    | ContinuousPartyTrick = 46
    | OncePerRound = 47
    | EndRoundEffect = 48

[<RequireQualifiedAccess>]
type ReferenceValueSource =
    | BadgeSides = 0
    | CardsChuckedByEffect = 1
    | Fixed = 2
    | KitCardsInOtherMitt = 3
    | MittCardsNeeded = 4
    | OtherAttachedVim = 5
    | OtherBoothCount = 6
    | OtherOcheDamageCounters = 7
    | OwnAttachedVim = 8
    | OwnBoothCount = 9
    | PrintedDamage = 10
    | QualifyingChuckedCards = 11
    | SelfDamageCounters = 12

[<RequireQualifiedAccess>]
type ReferenceSelection =
    | All = 0
    | AnyDistribution = 1
    | BeerMat = 2
    | Chosen = 3
    | OtherSideChosen = 4
    | Top = 5
    | UntilBlankSide = 6
    | UpTo = 7

[<RequireQualifiedAccess>]
type ReferenceLocation =
    | AttackingBloke = 0
    | BarChits = 1
    | LocalInPlay = 2
    | OtherBlokeChosen = 3
    | OtherBlokesAll = 4
    | OtherBoothAll = 5
    | OtherBoothChosen = 6
    | OtherMitt = 7
    | OtherOche = 8
    | OtherStack = 9
    | OwnAttachedBarKits = 10
    | OwnBlokeChosen = 11
    | OwnBlokesAll = 12
    | OwnBoothChosen = 13
    | OwnEmptiesTray = 14
    | OwnMitt = 15
    | OwnOche = 16
    | OwnStack = 17
    | Self = 18
    | KnockedOutBlokeAttachedVim = 19
    | OtherEmptiesTray = 20
    | OtherOcheAttachedVim = 21

[<RequireQualifiedAccess>]
type ReferenceDestination =
    | BottomOfOtherStack = 0
    | OtherBooth = 1
    | OtherMitt = 2
    | OtherStack = 3
    | OwnBooth = 4
    | OwnMitt = 5
    | OwnStack = 6

[<RequireQualifiedAccess>]
type ReferenceCondition =
    | AttachedVimCountsAreEqual = 0
    | BoothHasSpace = 1
    | FirstBeerMatIsBlankSide = 2
    | MatePlayedThisRound = 3
    | MittCountsAreEqual = 4
    | NamedBlokeInBooth = 5
    | NamedBlokeInPlay = 6
    | OpenedSecond = 7
    | Optional = 8
    | OtherBoothExists = 9
    | OtherOcheHasDamage = 10
    | OtherOcheHasMechanicalType = 11
    | OtherOcheHasRoughState = 12
    | OtherOcheIsBigHitter = 13
    | OtherOcheIsPromoted = 14
    | OtherSentHomeByThisAttackDamage = 15
    | OwnBarChitCountIsGreater = 16
    | OwnBlokeSentHomeByOtherAttackDamage = 17
    | OwnMittIsEmpty = 18
    | OwnersFirstRound = 19
    | PromotedFromMittThisRound = 20
    | SelfHasDamage = 21
    | SelfHasRoughState = 22
    | SelfHasVim = 23
    | SelfIsAtOche = 24
    | SelfIsInBooth = 25
    | SourceIsRegular = 26
    | TargetHasDamage = 27
    | TargetIsLandlord = 28
    | TargetIsRegular = 29
    | TargetIsSeasoned = 30

[<RequireQualifiedAccess>]
type ReferenceProgramKind =
    | Attack
    | PartyTrick
    | HouseRule

type ReferencePredicate =
    { Condition: ReferenceCondition
      MechanicalType: ReferenceMechanicalType voption
      RelatedId: string voption
      RoughState: ReferenceRoughState voption
      Value: int }

type ReferenceCardFilter =
    { Categories: ReferenceCardKind array
      Ranks: ReferenceRank array
      KitKinds: ReferenceKitKind array
      BasicVimOnly: bool
      DifferentMechanicalTypes: bool
      ExcludedRelatedIds: string array }

type ReferenceInstruction =
    { Opcode: ReferenceOpcode
      Amount: int
      ValueSource: ReferenceValueSource
      Targets: ReferenceLocation array
      Sources: ReferenceLocation array
      Destination: ReferenceDestination voption
      Selection: ReferenceSelection
      TargetCount: int
      SourceTopCount: int voption
      Predicates: ReferencePredicate array
      MechanicalTypes: ReferenceMechanicalType array
      RoughStates: ReferenceRoughState array
      RelatedIds: string array
      CardFilter: ReferenceCardFilter voption
      Then: ReferenceInstruction array
      Otherwise: ReferenceInstruction array }

type ReferenceAttack =
    { MechanicalId: string
      VimCost: ReferenceMechanicalType array
      PrintedDamage: int
      VariablePrintedDamage: bool
      CanBeUsedFromBench: bool
      Program: ReferenceInstruction array }

type ReferencePartyTrick =
    { MechanicalId: string
      Trigger: ReferenceTrigger
      Program: ReferenceInstruction array }

type ReferenceHouseRule =
    { MechanicalId: string
      Program: ReferenceInstruction array }

type ReferenceStackRules =
    { CardCount: int
      MechanicalCopyLimit: int
      BasicVimExempt: bool
      RequiresRegularBloke: bool }

type ReferenceOpeningRules =
    { OpeningParticipantSampledBeforeShuffle: bool
      MittSize: int
      OcheRegularCount: int
      BoothLimit: int
      BothMulliganNoBonus: bool
      OtherSideBonusPerExtraMulligan: bool
      OtherSideBonusOptional: bool
      BarChitCount: int
      OpeningParticipantMayAttack: bool
      OpeningParticipantMayPlayMate: bool }

type ReferenceRoundRules =
    { RequiredOpeningDraw: bool
      AttackEndsRound: bool
      PartyTricksAreNotAttacks: bool }

type ReferencePromotionRules =
    { ExactMechanicalEdgeRequired: bool
      NotOnEitherFirstRound: bool
      NotFirstRoundInPlay: bool
      NotTwiceInRound: bool
      RetainDamageAndAttachedCards: bool
      ClearRoughStatesAndAttackEffects: bool }

type ReferenceVimRules =
    { NormalAttachmentPerRound: int
      CostNotChuckedUnlessSpecified: bool
      LocalSatisfiedByAnyVim: bool }

type ReferenceKitRules =
    { BarKitsPerBloke: int
      MatesPerRound: int
      LocalsPerRound: int
      OneLocalInPlay: bool
      SameMechanicalLocalCannotReplace: bool
      NewLocalChucksOld: bool }

type ReferenceTaxiRules =
    { PerRound: int
      ChuckVimPerFareSymbol: bool
      RequiresBooth: bool
      MovingToBoothClearsRoughStatesAndAttackEffects: bool
      AttachedCardsAndDamageRemain: bool }

type ReferenceRoughStateRule =
    { State: ReferenceRoughState
      OcheOnly: bool
      CheckupDamageCounters: int
      CheckupBeerMat: bool
      BadgeSideRecovers: bool
      PreventsAttack: bool
      PreventsTaxi: bool
      RecoversAfterOwnersNextRound: bool voption
      BeforeAttackBeerMat: bool voption
      BlankSideCancelsAndSelfDamageCounters: int voption }

type ReferenceCheckupRules =
    { RoughStateOrder: ReferenceRoughState array
      OtherEffectsOutsideWholeBlock: bool
      CannotInterleave: bool
      SendHomeAfterBothChecks: bool }

type ReferenceSendHomeRules =
    { DamageAtLeastStayingPower: bool
      ChuckBlokeAndAttachedCards: bool
      NormalBarChits: int
      BigHitterBarChits: int
      OwnerPromotesFromBooth: bool }

type ReferenceWinRules =
    { Conditions: string array
      OneMethodEach: string
      MoreMethodsWins: string
      SuddenDeathBarChits: int
      RepeatUntilWinner: bool }

type ReferenceFossilRules =
    { KitIds: Set<string>
      PlayAsRegularLocalStayingPower: int
      CannotHaveRoughStates: bool
      CannotTaxi: bool
      MayChuckFromPlayDuringOwnersRound: bool
      SentHomeAwardsOneBarChit: bool }

type ReferenceBaseRules =
    { RulesVersion: string
      Stack: ReferenceStackRules
      Opening: ReferenceOpeningRules
      Round: ReferenceRoundRules
      Promotion: ReferencePromotionRules
      Vim: ReferenceVimRules
      Kit: ReferenceKitRules
      Taxi: ReferenceTaxiRules
      AttackOrder: string array
      DamageOrder: string array
      EffectDrawFromShortStack: string
      RequiredRoundDrawFromEmptyStack: string
      Checkup: ReferenceCheckupRules
      RoughStates: Map<ReferenceRoughState, ReferenceRoughStateRule>
      SendHome: ReferenceSendHomeRules
      Win: ReferenceWinRules
      Fossil: ReferenceFossilRules
      BigHitterIds: Set<string>
      OpcodeInventory: Set<ReferenceOpcode> }

type ReferenceCard =
    { Id: string
      Kind: ReferenceCardKind
      Rank: ReferenceRank voption
      StayingPower: int
      MechanicalTypes: ReferenceMechanicalType array
      PromotesFromId: string voption
      PromotesToIds: string array
      StackCopyLimit: int
      KitKind: ReferenceKitKind voption
      TaxiFare: int
      VimType: ReferenceMechanicalType voption
      BarChitsWhenSentHome: int
      SoftSpotMultipliers: Map<ReferenceMechanicalType, int>
      StubbornStreakReductions: Map<ReferenceMechanicalType, int>
      Attacks: ReferenceAttack array
      PartyTricks: ReferencePartyTrick array
      HouseRules: ReferenceHouseRule array }

type ReferenceProgram =
    { OwnerId: string
      Kind: ReferenceProgramKind
      MechanicalId: string
      Trigger: ReferenceTrigger voption
      Instructions: ReferenceInstruction array }

    member this.Key =
        let segment =
            match this.Kind with
            | ReferenceProgramKind.Attack -> "attack"
            | ReferenceProgramKind.PartyTrick -> "party-trick"
            | ReferenceProgramKind.HouseRule -> "house-rule"

        $"{this.OwnerId}/{segment}/{this.MechanicalId}"

type ReferenceAuthority =
    { ManifestVersion: string
      BaseRules: ReferenceBaseRules
      Cards: Map<string, ReferenceCard>
      Programs: ReferenceProgram array }

type ReferenceStarterDeck =
    { Id: string
      Entries: (string * int) array }

[<RequireQualifiedAccess>]
module ReferenceAuthority =

    let private fail (message: string) = raise (JsonException message)

    let private properties (element: JsonElement) =
        element.EnumerateObject() |> Seq.map _.Name |> Set.ofSeq

    let private expectObject
        (context: string)
        (required: Set<string>)
        (allowed: Set<string>)
        (element: JsonElement)
        =
        if element.ValueKind <> JsonValueKind.Object then
            fail $"The raw mechanical authority {context} is not an object."

        let actual = properties element
        let missing = Set.difference required actual
        let unknown = Set.difference actual allowed

        if not missing.IsEmpty || not unknown.IsEmpty then
            let missingText = missing |> String.concat ","
            let unknownText = unknown |> String.concat ","

            fail
                $"The raw mechanical authority {context} schema drifted (missing={missingText}; unknown={unknownText})."

    let private requiredProperty (context: string) (name: string) (element: JsonElement) =
        match element.TryGetProperty name with
        | true, value -> value
        | false, _ -> fail $"The raw mechanical authority {context} has no {name}."

    let private requiredString (context: string) (name: string) (element: JsonElement) =
        let value = requiredProperty context name element

        if value.ValueKind <> JsonValueKind.String then
            fail $"The raw mechanical authority {context}.{name} is not a string."

        match value.GetString() |> Option.ofObj with
        | None
        | Some "" -> fail $"The raw mechanical authority {context}.{name} is blank."
        | Some text when String.IsNullOrWhiteSpace text ->
            fail $"The raw mechanical authority {context}.{name} is blank."
        | Some text -> text

    let private nullableString (context: string) (name: string) (element: JsonElement) =
        let value = requiredProperty context name element

        match value.ValueKind with
        | JsonValueKind.Null -> ValueNone
        | JsonValueKind.String ->
            match value.GetString() |> Option.ofObj with
            | None
            | Some "" -> ValueNone
            | Some text when String.IsNullOrWhiteSpace text ->
                fail $"The raw mechanical authority {context}.{name} is blank."
            | Some text -> ValueSome text
        | _ -> fail $"The raw mechanical authority {context}.{name} is not nullable text."

    let private requiredInt (context: string) (name: string) (element: JsonElement) =
        let value = requiredProperty context name element

        if value.ValueKind <> JsonValueKind.Number then
            fail $"The raw mechanical authority {context}.{name} is not an integer."

        value.GetInt32()

    let private nullableInt (context: string) (name: string) (element: JsonElement) =
        let value = requiredProperty context name element

        match value.ValueKind with
        | JsonValueKind.Null -> ValueNone
        | JsonValueKind.Number -> ValueSome(value.GetInt32())
        | _ -> fail $"The raw mechanical authority {context}.{name} is not a nullable integer."

    let private requiredBool (context: string) (name: string) (element: JsonElement) =
        let value = requiredProperty context name element

        match value.ValueKind with
        | JsonValueKind.True -> true
        | JsonValueKind.False -> false
        | _ -> fail $"The raw mechanical authority {context}.{name} is not a boolean."

    let private nullableBool (context: string) (name: string) (element: JsonElement) =
        let value = requiredProperty context name element

        match value.ValueKind with
        | JsonValueKind.Null -> ValueNone
        | JsonValueKind.True -> ValueSome true
        | JsonValueKind.False -> ValueSome false
        | _ -> fail $"The raw mechanical authority {context}.{name} is not a nullable boolean."

    let private requiredArray (context: string) (name: string) (element: JsonElement) =
        let value = requiredProperty context name element

        if value.ValueKind <> JsonValueKind.Array then
            fail $"The raw mechanical authority {context}.{name} is not an array."

        value.EnumerateArray() |> Seq.toArray

    let private strings (context: string) (name: string) (element: JsonElement) =
        requiredArray context name element
        |> Array.mapi (fun index value ->
            if value.ValueKind <> JsonValueKind.String then
                fail $"The raw mechanical authority {context}.{name}[{index}] is not a string."

            match value.GetString() |> Option.ofObj with
            | None
            | Some "" -> fail $"The raw mechanical authority {context}.{name}[{index}] is blank."
            | Some text -> text)

    let private parseEnum<'value
        when 'value: struct and 'value :> ValueType and 'value: (new: unit -> 'value)>
        (context: string)
        (value: string)
        =
        let mutable parsed = Unchecked.defaultof<'value>

        if
            Enum.TryParse<'value>(value, false, &parsed)
            && String.Equals(string parsed, value, StringComparison.Ordinal)
        then
            parsed
        else
            fail $"The raw mechanical authority {context} has unknown value {value}."

    let private enumValue<'value
        when 'value: struct and 'value :> ValueType and 'value: (new: unit -> 'value)>
        (context: string)
        (name: string)
        (element: JsonElement)
        =
        requiredString context name element |> parseEnum<'value> $"{context}.{name}"

    let private enumValues<'value
        when 'value: struct and 'value :> ValueType and 'value: (new: unit -> 'value)>
        (context: string)
        (name: string)
        (element: JsonElement)
        =
        strings context name element
        |> Array.map (parseEnum<'value> $"{context}.{name}")

    let private nullableEnum<'value
        when 'value: struct and 'value :> ValueType and 'value: (new: unit -> 'value)>
        (context: string)
        (name: string)
        (element: JsonElement)
        =
        match nullableString context name element with
        | ValueNone -> ValueNone
        | ValueSome value -> ValueSome(parseEnum<'value> $"{context}.{name}" value)

    let private optionalProperty (name: string) (element: JsonElement) =
        match element.TryGetProperty name with
        | true, value -> ValueSome value
        | false, _ -> ValueNone

    let private optionalArray (context: string) (name: string) (element: JsonElement) =
        match optionalProperty name element with
        | ValueNone -> [||]
        | ValueSome value when value.ValueKind = JsonValueKind.Array ->
            value.EnumerateArray() |> Seq.toArray
        | ValueSome _ -> fail $"The raw mechanical authority {context}.{name} is not an array."

    let private optionalInt (context: string) (name: string) (element: JsonElement) =
        match optionalProperty name element with
        | ValueNone -> ValueNone
        | ValueSome value when value.ValueKind = JsonValueKind.Number -> ValueSome(value.GetInt32())
        | ValueSome _ -> fail $"The raw mechanical authority {context}.{name} is not an integer."

    let private predicate (context: string) (element: JsonElement) =
        let expected =
            Set [ "condition"; "mechanicalType"; "relatedId"; "roughState"; "value" ]

        expectObject context expected expected element

        { Condition = enumValue<ReferenceCondition> context "condition" element
          MechanicalType = nullableEnum<ReferenceMechanicalType> context "mechanicalType" element
          RelatedId = nullableString context "relatedId" element
          RoughState = nullableEnum<ReferenceRoughState> context "roughState" element
          Value = requiredInt context "value" element }

    let private cardFilter (context: string) (element: JsonElement) =
        let expected =
            Set
                [ "categories"
                  "ranks"
                  "kitKinds"
                  "basicVimOnly"
                  "differentMechanicalTypes"
                  "excludedRelatedIds" ]

        expectObject context expected expected element

        { Categories = enumValues<ReferenceCardKind> context "categories" element
          Ranks = enumValues<ReferenceRank> context "ranks" element
          KitKinds = enumValues<ReferenceKitKind> context "kitKinds" element
          BasicVimOnly = requiredBool context "basicVimOnly" element
          DifferentMechanicalTypes = requiredBool context "differentMechanicalTypes" element
          ExcludedRelatedIds = strings context "excludedRelatedIds" element }

    let rec private instructions (context: string) (program: JsonElement) =
        if program.ValueKind <> JsonValueKind.Array then
            fail $"The raw mechanical authority {context} is not an instruction array."

        program.EnumerateArray()
        |> Seq.mapi (fun index instruction ->
            let current = $"{context}[{index}]"

            let required =
                Set
                    [ "opcode"
                      "amount"
                      "valueSource"
                      "targets"
                      "selection"
                      "targetCount"
                      "predicates"
                      "mechanicalTypes"
                      "roughStates"
                      "relatedIds"
                      "then"
                      "otherwise" ]

            let allowed =
                Set.union
                    required
                    (Set [ "sources"; "destination"; "sourceTopCount"; "cardFilter" ])

            expectObject current required allowed instruction

            let sourceLocations =
                optionalArray current "sources" instruction
                |> Array.mapi (fun sourceIndex value ->
                    if value.ValueKind <> JsonValueKind.String then
                        fail
                            $"The raw mechanical authority {current}.sources[{sourceIndex}] is not text."

                    value.GetString()
                    |> Option.ofObj
                    |> Option.map (parseEnum<ReferenceLocation> $"{current}.sources")
                    |> Option.defaultWith (fun () ->
                        fail
                            $"The raw mechanical authority {current}.sources[{sourceIndex}] is null."))

            let destination =
                match optionalProperty "destination" instruction with
                | ValueNone -> ValueNone
                | ValueSome value when value.ValueKind = JsonValueKind.String ->
                    value.GetString()
                    |> Option.ofObj
                    |> Option.map (parseEnum<ReferenceDestination> $"{current}.destination")
                    |> Option.map ValueSome
                    |> Option.defaultWith (fun () ->
                        fail $"The raw mechanical authority {current}.destination is null.")
                | ValueSome _ ->
                    fail $"The raw mechanical authority {current}.destination is not text."

            let filter =
                match optionalProperty "cardFilter" instruction with
                | ValueNone -> ValueNone
                | ValueSome value -> ValueSome(cardFilter $"{current}.cardFilter" value)

            { Opcode = enumValue<ReferenceOpcode> current "opcode" instruction
              Amount = requiredInt current "amount" instruction
              ValueSource = enumValue<ReferenceValueSource> current "valueSource" instruction
              Targets = enumValues<ReferenceLocation> current "targets" instruction
              Sources = sourceLocations
              Destination = destination
              Selection = enumValue<ReferenceSelection> current "selection" instruction
              TargetCount = requiredInt current "targetCount" instruction
              SourceTopCount = optionalInt current "sourceTopCount" instruction
              Predicates =
                requiredArray current "predicates" instruction
                |> Array.mapi (fun predicateIndex value ->
                    predicate $"{current}.predicates[{predicateIndex}]" value)
              MechanicalTypes =
                enumValues<ReferenceMechanicalType> current "mechanicalTypes" instruction
              RoughStates = enumValues<ReferenceRoughState> current "roughStates" instruction
              RelatedIds = strings current "relatedIds" instruction
              CardFilter = filter
              Then = requiredProperty current "then" instruction |> instructions $"{current}.then"
              Otherwise =
                requiredProperty current "otherwise" instruction
                |> instructions $"{current}.otherwise" })
        |> Seq.toArray

    let private attacks (context: string) (element: JsonElement) =
        optionalArray context "attacks" element
        |> Array.mapi (fun index attack ->
            let current = $"{context}.attacks[{index}]"

            let expected =
                Set
                    [ "mechanicalId"
                      "presentationStatus"
                      "vimCost"
                      "printedDamage"
                      "variablePrintedDamage"
                      "canBeUsedFromBench"
                      "program" ]

            expectObject current expected expected attack

            { MechanicalId = requiredString current "mechanicalId" attack
              VimCost = enumValues<ReferenceMechanicalType> current "vimCost" attack
              PrintedDamage = requiredInt current "printedDamage" attack
              VariablePrintedDamage = requiredBool current "variablePrintedDamage" attack
              CanBeUsedFromBench = requiredBool current "canBeUsedFromBench" attack
              Program =
                requiredProperty current "program" attack |> instructions $"{current}.program" })

    let private partyTricks (context: string) (element: JsonElement) =
        optionalArray context "partyTricks" element
        |> Array.mapi (fun index trick ->
            let current = $"{context}.partyTricks[{index}]"
            let expected = Set [ "mechanicalId"; "presentationStatus"; "trigger"; "program" ]
            expectObject current expected expected trick

            { MechanicalId = requiredString current "mechanicalId" trick
              Trigger = enumValue<ReferenceTrigger> current "trigger" trick
              Program =
                requiredProperty current "program" trick |> instructions $"{current}.program" })

    let private houseRules (context: string) (element: JsonElement) =
        optionalArray context "houseRules" element
        |> Array.mapi (fun index rule ->
            let current = $"{context}.houseRules[{index}]"
            let expected = Set [ "mechanicalId"; "presentationStatus"; "program" ]
            expectObject current expected expected rule

            { MechanicalId = requiredString current "mechanicalId" rule
              Program =
                requiredProperty current "program" rule |> instructions $"{current}.program" })

    let private modifiers
        (context: string)
        (name: string)
        (element: JsonElement)
        (multiplier: bool)
        =
        requiredArray context name element
        |> Array.map (fun value ->
            let current = $"{context}.{name}"
            let expected = Set [ "mechanicalType"; "modifier" ]
            expectObject current expected expected value

            let mechanicalType =
                enumValue<ReferenceMechanicalType> current "mechanicalType" value

            let text = requiredString current "modifier" value

            let amount =
                if multiplier then
                    if not (text.StartsWith("×", StringComparison.Ordinal)) then
                        fail
                            $"The raw mechanical authority {current}.modifier is not a multiplier."

                    Int32.Parse(text[1..], Globalization.CultureInfo.InvariantCulture)
                else
                    abs (Int32.Parse(text, Globalization.CultureInfo.InvariantCulture))

            mechanicalType, amount)
        |> Map.ofArray

    let private collectible index (element: JsonElement) : ReferenceCard =
        let context = $"collectibles[{index}]"

        let expected =
            Set
                [ "id"
                  "approvedName"
                  "approvedType"
                  "presentationStatus"
                  "rank"
                  "stayingPower"
                  "mechanicalTypes"
                  "promotesFromId"
                  "promotesToIds"
                  "partyTricks"
                  "attacks"
                  "houseRules"
                  "softSpots"
                  "stubbornStreaks"
                  "taxiFare"
                  "barChitsWhenSentHome"
                  "productBucket"
                  "stackCopyLimit" ]

        expectObject context expected expected element

        { Id = requiredString context "id" element
          Kind = ReferenceCardKind.Bloke
          Rank = ValueSome(enumValue<ReferenceRank> context "rank" element)
          StayingPower = requiredInt context "stayingPower" element
          MechanicalTypes = enumValues<ReferenceMechanicalType> context "mechanicalTypes" element
          PromotesFromId = nullableString context "promotesFromId" element
          PromotesToIds = strings context "promotesToIds" element
          StackCopyLimit = requiredInt context "stackCopyLimit" element
          KitKind = ValueNone
          TaxiFare = requiredInt context "taxiFare" element
          VimType = ValueNone
          BarChitsWhenSentHome = requiredInt context "barChitsWhenSentHome" element
          SoftSpotMultipliers = modifiers context "softSpots" element true
          StubbornStreakReductions = modifiers context "stubbornStreaks" element false
          Attacks = attacks context element
          PartyTricks = partyTricks context element
          HouseRules = houseRules context element }

    let private kit index (element: JsonElement) : ReferenceCard =
        let context = $"kits[{index}]"

        let expected =
            Set
                [ "id"
                  "presentationStatus"
                  "kind"
                  "partyTricks"
                  "attacks"
                  "houseRules"
                  "stackCopyLimit"
                  "owned"
                  "pulled"
                  "traded"
                  "freelyAvailable" ]

        expectObject context expected expected element

        { Id = requiredString context "id" element
          Kind = ReferenceCardKind.Kit
          Rank = ValueNone
          StayingPower = 0
          MechanicalTypes = [||]
          PromotesFromId = ValueNone
          PromotesToIds = [||]
          StackCopyLimit = requiredInt context "stackCopyLimit" element
          KitKind = ValueSome(enumValue<ReferenceKitKind> context "kind" element)
          TaxiFare = 0
          VimType = ValueNone
          BarChitsWhenSentHome = 0
          SoftSpotMultipliers = Map.empty
          StubbornStreakReductions = Map.empty
          Attacks = attacks context element
          PartyTricks = partyTricks context element
          HouseRules = houseRules context element }

    let private vim index (element: JsonElement) : ReferenceCard =
        let context = $"basicVim[{index}]"

        let expected =
            Set
                [ "id"
                  "presentationStatus"
                  "mechanicalType"
                  "stackCopyLimit"
                  "owned"
                  "pulled"
                  "traded"
                  "freelyAvailable" ]

        expectObject context expected expected element

        { Id = requiredString context "id" element
          Kind = ReferenceCardKind.Vim
          Rank = ValueNone
          StayingPower = 0
          MechanicalTypes = [||]
          PromotesFromId = ValueNone
          PromotesToIds = [||]
          StackCopyLimit = requiredInt context "stackCopyLimit" element
          KitKind = ValueNone
          TaxiFare = 0
          VimType = ValueSome(enumValue<ReferenceMechanicalType> context "mechanicalType" element)
          BarChitsWhenSentHome = 0
          SoftSpotMultipliers = Map.empty
          StubbornStreakReductions = Map.empty
          Attacks = [||]
          PartyTricks = [||]
          HouseRules = [||] }

    let private programs (cards: ReferenceCard seq) =
        cards
        |> Seq.collect (fun card ->
            seq {
                for attack in card.Attacks do
                    yield
                        { OwnerId = card.Id
                          Kind = ReferenceProgramKind.Attack
                          MechanicalId = attack.MechanicalId
                          Trigger = ValueNone
                          Instructions = attack.Program }

                for trick in card.PartyTricks do
                    yield
                        { OwnerId = card.Id
                          Kind = ReferenceProgramKind.PartyTrick
                          MechanicalId = trick.MechanicalId
                          Trigger = ValueSome trick.Trigger
                          Instructions = trick.Program }

                for rule in card.HouseRules do
                    yield
                        { OwnerId = card.Id
                          Kind = ReferenceProgramKind.HouseRule
                          MechanicalId = rule.MechanicalId
                          Trigger = ValueNone
                          Instructions = rule.Program }
            })
        |> Seq.sortBy _.Key
        |> Seq.toArray

    let rec private opcodes (instructions: ReferenceInstruction seq) =
        instructions
        |> Seq.collect (fun instruction ->
            seq {
                yield instruction.Opcode
                yield! opcodes instruction.Then
                yield! opcodes instruction.Otherwise
            })

    let private baseRules (root: JsonElement) =
        let rules = requiredProperty "root" "baseRules" root

        let expected =
            Set
                [ "rulesVersion"
                  "stack"
                  "opening"
                  "round"
                  "promotion"
                  "vim"
                  "kit"
                  "taxi"
                  "attackOrder"
                  "damageOrder"
                  "damage"
                  "selectionRules"
                  "effectDrawFromShortStack"
                  "requiredRoundDrawFromEmptyStack"
                  "checkup"
                  "roughStates"
                  "roughStateCoexistence"
                  "sendHome"
                  "win"
                  "fossilKits"
                  "bigHitters"
                  "opcodeInventory" ]

        expectObject "baseRules" expected expected rules

        let objectOf name expectedProperties =
            let value = requiredProperty "baseRules" name rules
            expectObject $"baseRules.{name}" expectedProperties expectedProperties value
            value

        let stack =
            objectOf
                "stack"
                (Set
                    [ "cardCount"; "mechanicalCopyLimit"; "basicVimExempt"; "requiresRegularBloke" ])

        let opening =
            objectOf
                "opening"
                (Set
                    [ "openingParticipantSampledBeforeShuffle"
                      "mittSize"
                      "ocheRegularCount"
                      "boothLimit"
                      "mulligans"
                      "bothMulliganNoBonus"
                      "otherSideBonusPerExtraMulligan"
                      "otherSideBonusOptional"
                      "barChitCount"
                      "openingParticipantMayAttack"
                      "openingParticipantMayPlayMate" ])

        if requiredString "baseRules.opening" "mulligans" opening <> "RepeatUntilRegular" then
            fail "The raw mechanical authority has an unknown opening mulligan rule."

        let round =
            objectOf
                "round"
                (Set [ "requiredOpeningDraw"; "attackEndsRound"; "partyTricksAreNotAttacks" ])

        let promotion =
            objectOf
                "promotion"
                (Set
                    [ "exactMechanicalEdgeRequired"
                      "notOnEitherFirstRound"
                      "notFirstRoundInPlay"
                      "notTwiceInRound"
                      "retainDamageAndAttachedCards"
                      "clearRoughStatesAndAttackEffects" ])

        let vimRules =
            objectOf
                "vim"
                (Set
                    [ "normalAttachmentPerRound"
                      "costNotChuckedUnlessSpecified"
                      "localSatisfiedByAnyVim" ])

        let kitRules =
            objectOf
                "kit"
                (Set
                    [ "barBitsPerRound"
                      "barKitsPerRound"
                      "barKitsPerBloke"
                      "matesPerRound"
                      "localsPerRound"
                      "oneLocalInPlay"
                      "sameMechanicalLocalCannotReplace"
                      "newLocalChucksOld" ])

        for field in [ "barBitsPerRound"; "barKitsPerRound" ] do
            if requiredString "baseRules.kit" field kitRules <> "Unlimited" then
                fail $"The raw mechanical authority has an unknown {field} limit."

        let taxi =
            objectOf
                "taxi"
                (Set
                    [ "perRound"
                      "chuckVimPerFareSymbol"
                      "requiresBooth"
                      "movingToBoothClearsRoughStatesAndAttackEffects"
                      "attachedCardsAndDamageRemain" ])

        let sendHome =
            objectOf
                "sendHome"
                (Set
                    [ "damageAtLeastStayingPower"
                      "chuckBlokeAndAttachedCards"
                      "normalBarChits"
                      "bigHitterBarChits"
                      "ownerPromotesFromBooth" ])

        let checkup =
            objectOf
                "checkup"
                (Set
                    [ "roughStateOrder"
                      "otherEffectsOutsideWholeBlock"
                      "cannotInterleave"
                      "sendHomeAfterBothChecks" ])

        let win =
            objectOf
                "win"
                (Set
                    [ "conditions"
                      "oneMethodEach"
                      "moreMethodsWins"
                      "suddenDeathBarChits"
                      "repeatUntilWinner" ])

        let fossil =
            objectOf
                "fossilKits"
                (Set
                    [ "kitIds"
                      "playAsRegularLocalStayingPower"
                      "cannotHaveRoughStates"
                      "cannotTaxi"
                      "mayChuckFromPlayDuringOwnersRound"
                      "sentHomeAwardsOneBarChit" ])

        let bigHitters = objectOf "bigHitters" (Set [ "blokeIds" ])

        let roughStates =
            requiredArray "baseRules" "roughStates" rules
            |> Array.map (fun value ->
                let context = "baseRules.roughStates"

                let fields =
                    Set
                        [ "state"
                          "ocheOnly"
                          "checkupDamageCounters"
                          "checkupBeerMat"
                          "badgeSideRecovers"
                          "preventsAttack"
                          "preventsTaxi"
                          "recoversAfterOwnersNextRound"
                          "beforeAttackBeerMat"
                          "blankSideCancelsAndSelfDamageCounters" ]

                expectObject context fields fields value

                let state = enumValue<ReferenceRoughState> context "state" value

                state,
                { State = state
                  OcheOnly = requiredBool context "ocheOnly" value
                  CheckupDamageCounters = requiredInt context "checkupDamageCounters" value
                  CheckupBeerMat = requiredBool context "checkupBeerMat" value
                  BadgeSideRecovers = requiredBool context "badgeSideRecovers" value
                  PreventsAttack = requiredBool context "preventsAttack" value
                  PreventsTaxi = requiredBool context "preventsTaxi" value
                  RecoversAfterOwnersNextRound =
                    nullableBool context "recoversAfterOwnersNextRound" value
                  BeforeAttackBeerMat = nullableBool context "beforeAttackBeerMat" value
                  BlankSideCancelsAndSelfDamageCounters =
                    nullableInt context "blankSideCancelsAndSelfDamageCounters" value })
            |> Map.ofArray

        { RulesVersion = requiredString "baseRules" "rulesVersion" rules
          Stack =
            { CardCount = requiredInt "baseRules.stack" "cardCount" stack
              MechanicalCopyLimit = requiredInt "baseRules.stack" "mechanicalCopyLimit" stack
              BasicVimExempt = requiredBool "baseRules.stack" "basicVimExempt" stack
              RequiresRegularBloke = requiredBool "baseRules.stack" "requiresRegularBloke" stack }
          Opening =
            { OpeningParticipantSampledBeforeShuffle =
                requiredBool "baseRules.opening" "openingParticipantSampledBeforeShuffle" opening
              MittSize = requiredInt "baseRules.opening" "mittSize" opening
              OcheRegularCount = requiredInt "baseRules.opening" "ocheRegularCount" opening
              BoothLimit = requiredInt "baseRules.opening" "boothLimit" opening
              BothMulliganNoBonus = requiredBool "baseRules.opening" "bothMulliganNoBonus" opening
              OtherSideBonusPerExtraMulligan =
                requiredBool "baseRules.opening" "otherSideBonusPerExtraMulligan" opening
              OtherSideBonusOptional =
                requiredBool "baseRules.opening" "otherSideBonusOptional" opening
              BarChitCount = requiredInt "baseRules.opening" "barChitCount" opening
              OpeningParticipantMayAttack =
                requiredBool "baseRules.opening" "openingParticipantMayAttack" opening
              OpeningParticipantMayPlayMate =
                requiredBool "baseRules.opening" "openingParticipantMayPlayMate" opening }
          Round =
            { RequiredOpeningDraw = requiredBool "baseRules.round" "requiredOpeningDraw" round
              AttackEndsRound = requiredBool "baseRules.round" "attackEndsRound" round
              PartyTricksAreNotAttacks =
                requiredBool "baseRules.round" "partyTricksAreNotAttacks" round }
          Promotion =
            { ExactMechanicalEdgeRequired =
                requiredBool "baseRules.promotion" "exactMechanicalEdgeRequired" promotion
              NotOnEitherFirstRound =
                requiredBool "baseRules.promotion" "notOnEitherFirstRound" promotion
              NotFirstRoundInPlay =
                requiredBool "baseRules.promotion" "notFirstRoundInPlay" promotion
              NotTwiceInRound = requiredBool "baseRules.promotion" "notTwiceInRound" promotion
              RetainDamageAndAttachedCards =
                requiredBool "baseRules.promotion" "retainDamageAndAttachedCards" promotion
              ClearRoughStatesAndAttackEffects =
                requiredBool "baseRules.promotion" "clearRoughStatesAndAttackEffects" promotion }
          Vim =
            { NormalAttachmentPerRound =
                requiredInt "baseRules.vim" "normalAttachmentPerRound" vimRules
              CostNotChuckedUnlessSpecified =
                requiredBool "baseRules.vim" "costNotChuckedUnlessSpecified" vimRules
              LocalSatisfiedByAnyVim =
                requiredBool "baseRules.vim" "localSatisfiedByAnyVim" vimRules }
          Kit =
            { BarKitsPerBloke = requiredInt "baseRules.kit" "barKitsPerBloke" kitRules
              MatesPerRound = requiredInt "baseRules.kit" "matesPerRound" kitRules
              LocalsPerRound = requiredInt "baseRules.kit" "localsPerRound" kitRules
              OneLocalInPlay = requiredBool "baseRules.kit" "oneLocalInPlay" kitRules
              SameMechanicalLocalCannotReplace =
                requiredBool "baseRules.kit" "sameMechanicalLocalCannotReplace" kitRules
              NewLocalChucksOld = requiredBool "baseRules.kit" "newLocalChucksOld" kitRules }
          Taxi =
            { PerRound = requiredInt "baseRules.taxi" "perRound" taxi
              ChuckVimPerFareSymbol = requiredBool "baseRules.taxi" "chuckVimPerFareSymbol" taxi
              RequiresBooth = requiredBool "baseRules.taxi" "requiresBooth" taxi
              MovingToBoothClearsRoughStatesAndAttackEffects =
                requiredBool "baseRules.taxi" "movingToBoothClearsRoughStatesAndAttackEffects" taxi
              AttachedCardsAndDamageRemain =
                requiredBool "baseRules.taxi" "attachedCardsAndDamageRemain" taxi }
          AttackOrder = strings "baseRules" "attackOrder" rules
          DamageOrder = strings "baseRules" "damageOrder" rules
          EffectDrawFromShortStack = requiredString "baseRules" "effectDrawFromShortStack" rules
          RequiredRoundDrawFromEmptyStack =
            requiredString "baseRules" "requiredRoundDrawFromEmptyStack" rules
          Checkup =
            { RoughStateOrder =
                enumValues<ReferenceRoughState> "baseRules.checkup" "roughStateOrder" checkup
              OtherEffectsOutsideWholeBlock =
                requiredBool "baseRules.checkup" "otherEffectsOutsideWholeBlock" checkup
              CannotInterleave = requiredBool "baseRules.checkup" "cannotInterleave" checkup
              SendHomeAfterBothChecks =
                requiredBool "baseRules.checkup" "sendHomeAfterBothChecks" checkup }
          RoughStates = roughStates
          SendHome =
            { DamageAtLeastStayingPower =
                requiredBool "baseRules.sendHome" "damageAtLeastStayingPower" sendHome
              ChuckBlokeAndAttachedCards =
                requiredBool "baseRules.sendHome" "chuckBlokeAndAttachedCards" sendHome
              NormalBarChits = requiredInt "baseRules.sendHome" "normalBarChits" sendHome
              BigHitterBarChits = requiredInt "baseRules.sendHome" "bigHitterBarChits" sendHome
              OwnerPromotesFromBooth =
                requiredBool "baseRules.sendHome" "ownerPromotesFromBooth" sendHome }
          Win =
            { Conditions = strings "baseRules.win" "conditions" win
              OneMethodEach = requiredString "baseRules.win" "oneMethodEach" win
              MoreMethodsWins = requiredString "baseRules.win" "moreMethodsWins" win
              SuddenDeathBarChits = requiredInt "baseRules.win" "suddenDeathBarChits" win
              RepeatUntilWinner = requiredBool "baseRules.win" "repeatUntilWinner" win }
          Fossil =
            { KitIds = strings "baseRules.fossilKits" "kitIds" fossil |> Set.ofArray
              PlayAsRegularLocalStayingPower =
                requiredInt "baseRules.fossilKits" "playAsRegularLocalStayingPower" fossil
              CannotHaveRoughStates =
                requiredBool "baseRules.fossilKits" "cannotHaveRoughStates" fossil
              CannotTaxi = requiredBool "baseRules.fossilKits" "cannotTaxi" fossil
              MayChuckFromPlayDuringOwnersRound =
                requiredBool "baseRules.fossilKits" "mayChuckFromPlayDuringOwnersRound" fossil
              SentHomeAwardsOneBarChit =
                requiredBool "baseRules.fossilKits" "sentHomeAwardsOneBarChit" fossil }
          BigHitterIds = strings "baseRules.bigHitters" "blokeIds" bigHitters |> Set.ofArray
          OpcodeInventory =
            enumValues<ReferenceOpcode> "baseRules" "opcodeInventory" rules |> Set.ofArray }

    let private validateIdentities
        (cards: ReferenceCard array)
        (programRows: ReferenceProgram array)
        =
        let duplicates values =
            values
            |> Seq.countBy id
            |> Seq.choose (fun (value, count) -> if count = 1 then None else Some value)
            |> Seq.toArray

        if duplicates (cards |> Seq.map _.Id) |> Array.isEmpty |> not then
            fail "The raw mechanical authority has duplicate card identities."

        if duplicates (programRows |> Seq.map _.Key) |> Array.isEmpty |> not then
            fail "The raw mechanical authority has duplicate program identities."

        let cardIds = cards |> Seq.map _.Id |> Set.ofSeq

        for card in cards do
            match card.Kind with
            | ReferenceCardKind.Bloke when
                not (card.Id.StartsWith("BLK-", StringComparison.Ordinal))
                ->
                fail
                    $"The raw mechanical authority card identity {card.Id} does not match its kind."
            | ReferenceCardKind.Kit when not (card.Id.StartsWith("KIT-", StringComparison.Ordinal)) ->
                fail
                    $"The raw mechanical authority card identity {card.Id} does not match its kind."
            | ReferenceCardKind.Vim when not (card.Id.StartsWith("VIM-", StringComparison.Ordinal)) ->
                fail
                    $"The raw mechanical authority card identity {card.Id} does not match its kind."
            | _ -> ()

            match card.PromotesFromId with
            | ValueSome source when not (cardIds.Contains source) ->
                fail $"The raw mechanical authority promotion source {source} is unknown."
            | _ -> ()

            for target in card.PromotesToIds do
                if not (cardIds.Contains target) then
                    fail $"The raw mechanical authority promotion target {target} is unknown."

    let load (path: string) =
        use document = JsonDocument.Parse(File.ReadAllText path)
        let root = document.RootElement

        let rootFields =
            Set
                [ "manifestVersion"
                  "presentationStatus"
                  "approvedMechanicalDisplayMap"
                  "collectibles"
                  "kits"
                  "basicVim"
                  "products"
                  "baseRules" ]

        expectObject "root" rootFields rootFields root

        let cards =
            Array.concat
                [ requiredArray "root" "collectibles" root |> Array.mapi collectible
                  requiredArray "root" "kits" root |> Array.mapi kit
                  requiredArray "root" "basicVim" root |> Array.mapi vim ]

        let programRows = programs cards
        validateIdentities cards programRows
        let rules = baseRules root

        let usedOpcodes =
            programRows |> Seq.collect (fun row -> opcodes row.Instructions) |> Set.ofSeq

        if usedOpcodes <> rules.OpcodeInventory then
            fail "The raw program opcodes and base-rule opcode inventory disagree."

        { ManifestVersion = requiredString "root" "manifestVersion" root
          BaseRules = rules
          Cards = cards |> Seq.map (fun card -> card.Id, card) |> Map.ofSeq
          Programs = programRows }

    let loadStarterDecks (path: string) =
        use document = JsonDocument.Parse(File.ReadAllText path)
        let root = document.RootElement

        let rootFields =
            Set [ "schemaVersion"; "starterDeckVersion"; "mechanicalManifestVersion"; "decks" ]

        expectObject "starter deck root" rootFields rootFields root

        if requiredInt "starter deck root" "schemaVersion" root <> 1 then
            fail "The starter deck schema is unsupported."

        requiredArray "starter deck root" "decks" root
        |> Array.mapi (fun index deck ->
            let context = $"starter deck[{index}]"

            let fields =
                Set
                    [ "id"
                      "savedDeckId"
                      "name"
                      "type"
                      "role"
                      "description"
                      "leaderCardId"
                      "entries" ]

            expectObject context fields fields deck

            { Id = requiredString context "id" deck
              Entries =
                requiredArray context "entries" deck
                |> Array.mapi (fun entryIndex entry ->
                    let entryContext = $"{context}.entries[{entryIndex}]"
                    let entryFields = Set [ "cardId"; "quantity" ]
                    expectObject entryContext entryFields entryFields entry

                    requiredString entryContext "cardId" entry,
                    requiredInt entryContext "quantity" entry) })
        |> Array.sortBy _.Id

    let expandStarterDeck (deck: ReferenceStarterDeck) =
        deck.Entries
        |> Array.collect (fun (cardId, quantity) -> Array.create quantity cardId)

    let programOwnerIds (authority: ReferenceAuthority) =
        authority.Programs |> Seq.map _.OwnerId |> Set.ofSeq
