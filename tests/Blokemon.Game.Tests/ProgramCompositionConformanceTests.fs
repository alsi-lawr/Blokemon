namespace Blokemon.Game.Tests

open System
open System.Collections.Immutable
open System.IO
open System.Security.Cryptography
open System.Text.Json
open Blokemon.App
open Blokemon.App.Contracts
open Blokemon.Core.SetDesign
open Blokemon.Game
open FsUnit
open TUnit.Core
open ConformanceCensus

[<AutoOpen>]
module internal ProgramCompositionConformanceFixtures =

    let private withFirstPlayerRounds rounds (state: MatchState) =
        { state with
            Players =
                ImmutableArray.CreateRange(
                    state.Players
                    |> Seq.map (fun player ->
                        if player.Id = MatchScenario.FirstPlayer then
                            { player with RoundsStarted = rounds }
                        else
                            player)
                ) }

    let private executionBytes (execution: Execution) =
        Array.concat
            [ JsonSerializer.SerializeToUtf8Bytes(execution.State, MatchJson.Options)
              JsonSerializer.SerializeToUtf8Bytes(execution.Events, MatchJson.Options) ]

    let private activatedState (row: ProgramRow) =
        let state = richBattleState row.OwnerId

        match row.MechanicalId with
        | "BLK-053-T01" ->
            let mate =
                { state.Card(CardInstanceId "own-stack-kit") with
                    MechanicalId = MechanicalCardId "KIT-010" }

            MatchScenario.WithCards state [ mate ]
        | "BLK-132-T01" -> withFirstPlayerRounds 1 state
        | "BLK-151-T01" ->
            { state with
                Cards =
                    ImmutableArray.CreateRange(
                        state.Cards
                        |> Seq.filter (fun card ->
                            card.Id <> CardInstanceId "own-mitt-bloke"
                            && card.Id <> CardInstanceId "own-mitt-kit")
                    ) }
        | _ -> state

    let private compositionBytes (row: ProgramRow) =
        if declarativeKitStructuralProgramIds.Contains row.MechanicalId then
            raise (
                InvalidOperationException(
                    $"Declarative program row {row.MechanicalId} has no MatchEngine composition route."
                )
            )
        else
            match row.Kind, row.Trigger with
            | ProgramKind.Attack, _ ->
                executeAttack (richBattleState row.OwnerId) row.MechanicalId |> executionBytes
            | ProgramKind.HouseRule, _ ->
                executeAction (kitState row.OwnerId) (playsKit (CardInstanceId "kit-under-test"))
                |> executionBytes
            | ProgramKind.PartyTrick, ValueSome BlokemonTrigger.Activated ->
                executeAction (activatedState row) (usesPartyTrick row.MechanicalId)
                |> executionBytes
            | ProgramKind.PartyTrick, ValueSome BlokemonTrigger.Continuous ->
                executeAction
                    (continuousState row.OwnerId)
                    (playsBloke (CardInstanceId "own-mitt-bloke"))
                |> executionBytes
            | ProgramKind.PartyTrick, ValueSome BlokemonTrigger.OnPromotionFromMitt ->
                let promotion =
                    MatchScenario.Authority.Collectibles
                    |> Array.find (fun card -> card.Id = row.OwnerId)

                executeAction
                    (promotionState promotion)
                    (promotes (CardInstanceId "promotion") (CardInstanceId "attacker"))
                |> executionBytes
            | ProgramKind.PartyTrick, ValueSome trigger ->
                observeReactiveTrigger (MatchScenario.Engine()) trigger |> BitConverter.GetBytes
            | ProgramKind.PartyTrick, ValueNone ->
                failwith $"Party Trick {row.MechanicalId} had no trigger."

    let compositionHash row =
        row
        |> compositionBytes
        |> SHA256.HashData
        |> Convert.ToHexString
        |> _.ToLowerInvariant()

    // These are semantic MatchEngine snapshots, generated only after each row's authoritative
    // scenario was reviewed. The explicit generator below makes review updates reproducible.
    let expectedCompositionHashes: Map<string, string> =
        [ "BLK-001-B01", "2094d5d4c48938e8806dfb2e7b0760759271741bd43ba93e487b26d2fddf31fd"
          "BLK-002-B01", "2a7c86ef495187f06b15545cbfd5ba19cb745055d6af291e9b76b4d6b391f6be"
          "BLK-003-B01", "da2454b74b2cfb4134a14aaa311ae33e57eeb7691b79218f053448476fcdb792"
          "BLK-003-T01", "ccc0b16c675a90ba9aa2c20dd780088acf30d5dd35cce8877c175be951c7e0b8"
          "BLK-005-B02", "203152a7c385cf70c8b544b1056071bd20de20648ddaac0478b73294ba225560"
          "BLK-006-B01", "20c08b823c9baacf24976908444c7d0cdd6f22f52b3ed1c7adc5874410ad72d5"
          "BLK-006-B02", "ace4943b69122c0c1dea332ea18a39b165739c4fc4ae081b1b7d14ee061290bc"
          "BLK-007-B01", "5de94e743b2de4afc81dd1b0825eeb4259bcba8cab0893686a37bf1cb17372f5"
          "BLK-008-B01", "578c8fa520e37f659eb33ec584e936c26994838c282050390cae6049382e2782"
          "BLK-009-B01", "dbba4ae4bb0e981e1c4a6d073216bd75d9fb53e8ab08287cb85d19a13b17f9a1"
          "BLK-009-T01", "a1300761c0f622bcb41dc0f02d338fcfc992f3427fdbe181630cf1bbc4ba94c6"
          "BLK-010-B01", "f27914d7bc2da609563ab5e74ee28a94723957cf22c8e9900042b119dc4fd5db"
          "BLK-011-B02", "d548eaec3d2755fc6b8a3b4188561619eb5c55cb7a98bf4aef3b05b57a7dc143"
          "BLK-012-B01", "c9eb92e5cb30a805581fcd702902252de1bc096da7d1f0be2631e0927dd2d087"
          "BLK-012-B02", "27ea653d619e0f16f1720c2ac6bca06229d576b50a6a63a6f02c00cdd13f63f6"
          "BLK-014-T01", "794a51666c4362ab494431e17d08dcad3ce66bcdb8544718dcc575fb564e00fe"
          "BLK-015-B01", "aba6518c71937e734f446f69ba02a4ed94c0eb10b0add92ca19033eed9cbcb4d"
          "BLK-016-B01", "39abb83b725533a0eb456c841d68eaae671b8f00d71de55db64cafbd212511fb"
          "BLK-018-B02", "f488c09cbc6458db642692dbb9f1a8a194f9afe596cf4d06e8eccaf695c49fbc"
          "BLK-019-B01", "7946631d27668bcbdd51812dfcaccaca596c22e28ebc76979347e2e44bb95606"
          "BLK-020-B01", "7a3a40e1aa1d31cbda0181694e6a8a4cc6389900eea53a0e87c79cbfbf2637e0"
          "BLK-021-T01", "805a87153739d34473cb33aaa46382922900a43ef40522c37efdd8a8001905e7"
          "BLK-022-B01", "47b2078f0dcc6740be8849542dfb4541483180b3f7baa5a522eafe1d620160fb"
          "BLK-023-B01", "59513245844a4de29489d6c8645a205f7c6aea5d96743edd56c2fa8f0c74bdc7"
          "BLK-024-B01", "82754c455ce8d2403c5cf51f6cabda516a0765dc48c8fd1fd5931d22d0fe9f27"
          "BLK-024-B02", "b25cbf6001d7ed51871099177e2dd228bf43915998e80e58bdb60f5c3f9dfc3f"
          "BLK-025-B01", "043f8241ac455067369edc5a70c81a2fd3b28fc654ab62b19aba0affbbfd0922"
          "BLK-026-B01", "ee32cb61805d1ea8c0496757e42b6688c0a4c51d8f8a0c14f8c9d1aa39c64650"
          "BLK-026-T01", "df3f619804a92fdb4057192dc43dd748ea778adc52bc498ce80524c014b81119"
          "BLK-027-T01", "e6f7d167dbff214a06bbc79b025103ef0f7382eee9fba46e179420b0fe12dd0d"
          "BLK-028-B01", "2006dec6ca891567e3c165cf5043fd3e3a654b6f381fd1cdb7c65ed691d63ad7"
          "BLK-028-B02", "dbce15c90c055e69f2d7383ce29f0107d0b014562df419cb01eedc39d5563580"
          "BLK-029-B01", "c459605e6b068bdda7693979d7e582b9413555225464694d1dbf8195cd9024e2"
          "BLK-030-B01", "1ad80e8b7a93886b87ff8dbdc6e56bfe509e6db6b84b3ee1b3f3534c7c00c0fc"
          "BLK-031-B01", "3398977db60888a4ddd2d9546bb9c762b22b26c2757752312181acfa8a1caf8a"
          "BLK-034-B01", "8e04dbc8913575999b269954bbccfec976ab1cb3d82d6b3a6728aaf360b66689"
          "BLK-034-T01", "98948b7ed28538e7383eb49f94603ed667ff1ce1731343093de5314eb96ec34c"
          "BLK-035-B01", "c97903f1960cce5ef00e0efd082583b51dfce9ad8926871f7a45bf8d20906294"
          "BLK-036-B02", "dfb918ba7a88ffdbf26bc82208b56f2d910a5c1c02f5dba61e63475021359a2b"
          "BLK-037-B01", "2d289cfeffc01355c7953f8a7f8dd9353571a1d1e546e9c25c5a21283f6bd6e3"
          "BLK-038-B01", "67f744096006d9f07c5e9ea259150094b9e3d8d6cd5c05dc1c613238645de2df"
          "BLK-038-B02", "cfd23aa70accff23b3b5d25e115d10154ae50acd8cba6f4681b26e5de9448d49"
          "BLK-039-B01", "439e04adc2486718b8f356ee7bae2db6586f4f112d1383e5121d67c27ffd058b"
          "BLK-039-B02", "98110e313c68ea73c29b64e441dee3cc969614dbe819b3d4c877db67bd504db4"
          "BLK-040-B02", "e45ae1c939f27b98cc7c115b051ba962e11d714942f131cf0f4bb5976f085e11"
          "BLK-041-T01", "3bf6c3492d244f75fd3fb091a1d819d2c05672b40f2e9609b553c45bfa83c6c5"
          "BLK-044-T01", "a3b362674bc185153a271c48f49e2f134418d9c843355937bbda51b5c211c82b"
          "BLK-045-T01", "53b781faf12ddf02b273018048f7a7d7aebb34f4f7777ddb2df19c5b61a74c64"
          "BLK-046-B02", "713ae43085086e21ae5cfb813460d1da4818b16de09442cefcb6aaf614b2cd33"
          "BLK-047-B01", "de2688c19bb7d23cbb99cb08db8b3bd2dd6cb2bd945bb1520dfb920d787effcf"
          "BLK-049-B01", "cb3d41073c095abb8985743e11df893b239f16e62d9f59329e30d10a164829a4"
          "BLK-052-B01", "8f44fa2c14d6b606218d8a9978b2b632a6e00c3f41e28c4fde95f2e231666c99"
          "BLK-053-T01", "f4fedef86b7ed96d67212679e09b6a81366c1aa370e9787c8e3e62379b7defa7"
          "BLK-055-B01", "ba027815b7ffda97f4e2c0e889b2930c5816a3b6551e8f93ed2d28341ad47662"
          "BLK-056-B01", "44c61482402da9acb76417a8e68b2029e5c821ae5479b0dcb251fffa4273493c"
          "BLK-057-B01", "0d39f9a7fc9c5b7309e8399a5d7ab2ed925eec985a761d248e84bf1585628be7"
          "BLK-057-B02", "7387e351c1448333a8b7a574db1df2cdedecddc394c6be07a78aa6df053bc86e"
          "BLK-058-B01", "8ef6ef91f9070eef30b0381b0fe687fcdda5e8c400baf309fc71bc23c3f7fff0"
          "BLK-059-B01", "4d4029f02ef56f6e35f6ef313ca1ea3b1162099ef74d1eb2b1ad75b552f18b7e"
          "BLK-059-B02", "256cbe431697d3d5f98453b65a4e4efa08d3592529868b74a9eed73d84b68781"
          "BLK-060-B01", "2f2775fd8f2ac3616e515ee137d3dcd1e317fa4cc1eb39cb464cfec7294c45fc"
          "BLK-061-B02", "e855c5e7557ca03af988eeef8416b64820cf7210eb111aa6b0b539bfaba4d76d"
          "BLK-062-B01", "ef7b347be4d5b6b69d361f5a2d8c338cfa024eb6d728ac93e5c5c98475611b2c"
          "BLK-062-B02", "cac87fc317d9bdf60b7929e4d9ee0fbad3027f7f79f6a407eceed13b0fba4df9"
          "BLK-064-B01", "30c5aa0b36b6bbc3de3b7b061b2f70569e63138fac7b080b6bfdc8b8dccfbbc4"
          "BLK-065-B01", "99864ae5e1da8aac8704259345ff39858d9b8886147d0b02b362854f9e9ed2e1"
          "BLK-067-B01", "91cd1e21952f58ede18e39e508b41a4b2b7b918bbd3bf7692319972f4f6d00fd"
          "BLK-068-B01", "5cb470df6af3518cc634346b280bc0fa950697ce02d47b1ed878c6d3c188c8d0"
          "BLK-068-T01", "eed72f47b9a4ba7d6f139f8f731768824bb7a19f9a3882d1db4337da657a0c76"
          "BLK-069-B02", "09966e7665348ad377856325f28c8635c2564ce72733ac9fbf5dbac597541d4d"
          "BLK-071-B02", "ecdf80eb7e9966f38084f31d682f333904a31571f071af87fb8ddfbeb611acde"
          "BLK-073-B01", "2f8bd039e87890d64b4c037787e74f684e2dd35bff141f9306415ed38a3fb86a"
          "BLK-073-B02", "b3f3bb0de5e1faa71e32554cb6ee0667325d6d6ec850ffe230c6e6b1f3b5c4c0"
          "BLK-074-B01", "d4a4a84431db3066fa0bcfd576a221a13647f7280373d6b62419e31c875e8e61"
          "BLK-075-B01", "bf8e5d4316f91db2790e6fd2fa49ec45ba8c14ce4907ff7f6f2e18160a4e8ad3"
          "BLK-076-B01", "ce9e646a8bc16d05a63eb9679ecc8fe2e5e87b70fa559b880407de7853cb9165"
          "BLK-076-B02", "6eecb2d254b8eb931378af140b63abf9eeb7b3515bf8e071d64f2bab6283e0b9"
          "BLK-078-B02", "0e684252fa8079ce08a304e4c761b0776f28dd22f7b83022b4e78abb34335c8c"
          "BLK-079-B01", "af573acc6c594b185e85e6c2ed3920f3048c9e2cc953d680eee52e8c8df2e75a"
          "BLK-080-B02", "5721fed47634d558ed061f0b5449f139b93b00a5e90d3cea6139be8a755dc238"
          "BLK-081-B02", "f3ef145268a9b0e01b02968e01d7f87920e6651b95d09e9fc1cfcbdf15df1812"
          "BLK-082-B01", "b75fd4cb4bdf520a8a9f6ea8b64c41de56c7650d7574044cea0037cb377c518c"
          "BLK-084-B01", "9fc5bec2718e1fca24e49dd4a582910307bb83f578e57935569aebb728951443"
          "BLK-085-B01", "db4ff47aef8517ba37688ee1cf8f7188da962de5dc2c066fd8f482192b1dbcba"
          "BLK-085-T01", "0ac5f523be1155e75e29c587a62121d6dd748d45f91b677e7813eb53133c4c44"
          "BLK-088-B01", "3629f6e0f1dd9d5b2299eca846d299f6ccff1879378665abda8bdfa2d7dc9fff"
          "BLK-089-B01", "f5b762b1b4a268b16b0fa754ae3d4c2c3a23c31f60892ea7cae2fca50c85874c"
          "BLK-090-B01", "397278cae9b2c59e4ea0e1fce9e99cb33392e9dce44b35314e0ba4d61bb408dd"
          "BLK-091-B01", "fdadf052da8d5fbac421c85fae673814479273fba72d646b043358d5cacac474"
          "BLK-093-T01", "291bcf8796a76c42c80111f513150a5efd07c7c1825fb13f54a555d5b811fc9e"
          "BLK-094-B01", "800079ace2263b6dc565c9c752e546b2c44cd8fbc7f0f666d4b04c27b7928a24"
          "BLK-094-B02", "c843eae399f02b98bf546e55985ea56ea3ff223bed9d6abbfb9e5fb51d229144"
          "BLK-095-B01", "ba07aed35ff61b08da40d7c4f23d02d7831d02e647cbbbb4477824febe2cc87e"
          "BLK-097-T01", "ba6e71d3f2f6402dd022d53b79e7890c9b8c19ad5afc845b937ca4ff6e247498"
          "BLK-098-B01", "f04db4555810d4a33e45314297f0296c3a9175b88860b6b1a1b48e407f5b2381"
          "BLK-099-B01", "a50affb31b006a9e9ba751ee559e94621656329544e49feaa5d1b8aa0c2604cd"
          "BLK-100-B01", "d0c1db4cc18f44b5d9b76ccdd531a17128b9d57b329ddcd4be5a1abf065fae2d"
          "BLK-101-B01", "71e110652d2629a4a272edb69806450c5f43a671023256a84dfa4c631a36fa3e"
          "BLK-102-B01", "3085e5576e39503d6a503e7fca87c3de7a45de9c2444b31f24a3dc277b54958f"
          "BLK-103-B01", "14ccc0a31f24b52d1c2ba748d78c3fb14dc22ff0ec4840edc5444896f7505181"
          "BLK-104-B01", "0da8ac394e5b6e41153a551fe80080de1da4873e08387cf86d726556073d36ba"
          "BLK-104-T01", "76ad8f720db7ea33626a7af67472ae046e7db1d817b702dc9ebabb683c03d849"
          "BLK-105-B01", "96c7a4e86cc1a4cd096696dc72e0eeac81624571c472845fa15bf5689070d11b"
          "BLK-105-B02", "4072a30de2d0bf052c16f3901a87f411632d8d79005e9da9e0c561774441b7fe"
          "BLK-106-B01", "b8660dff0247c3101ee90d10148052fbd001a60c911ed1241cfda4054909ca11"
          "BLK-107-B01", "0c6e69ab7dc187cfe1ecf945899fdc719e100155a89b83588bcb95a8a2cde8ea"
          "BLK-107-T01", "4f5e1d312b4d1bb8ccaf069c18cddeca414ae78160fb3c793ffc730eef4e4f17"
          "BLK-108-B01", "3941e63643bc2a22c8b4944914d43355388a62f95f1c4007d5a1a0eb872d0b2f"
          "BLK-109-B01", "c2b5d5780a366771a26d81bf33df6a785939a0f44d10a174141c228f67a7821b"
          "BLK-110-B01", "082eac253c6907796af58623690b170b942525d7d014756313b5e40542e001ae"
          "BLK-110-T01", "df3f619804a92fdb4057192dc43dd748ea778adc52bc498ce80524c014b81119"
          "BLK-111-B01", "bc6e770f9cab6f4103255800d9bd00e068808c91349631025b676264f1736e3b"
          "BLK-112-B02", "655cfdc46c25b3588df9e2be084536422d75438ef9f4af9395ad9da6bb1edc4f"
          "BLK-113-T01", "67abdd721024f0ff4e0b3f4c2fc13bc5bad42d0b7851d456d88d203d15aaa450"
          "BLK-114-B01", "2ab02491c35616da94f145eea2fed7209889387fde64e7fd400a5d34c6956cb9"
          "BLK-115-B02", "19b3e069b027e975f7dfd828938f220463e782b543a44ef250c7e763903d01e9"
          "BLK-117-B01", "c711c473db0a8c2f7a817dfafc3d359c84670c471175c9c71677b856840dfeb9"
          "BLK-118-B01", "0ff061746fb4d92aa3e4143a8b6b4afa4414884edcf167ec20327b57fe953612"
          "BLK-119-B01", "9dd9514c7e7c1d4b147feaa30a76fb1713843618b7212c82e570fa67689a86db"
          "BLK-119-B02", "33c832933069148c529f13ae91db1deafcc809f77a78bc24c66ecfefd91fad69"
          "BLK-120-B01", "3323cbb151f75407c0a8f42a4184a1d66163983654dcd81706cbdbac170c114c"
          "BLK-121-T01", "5eda8f6fc236be61d22094b2550b9b75e6a5f165e28e62286709124a7bee70f5"
          "BLK-122-T01", "1567c418eaf0af8f40ed510fec0aaff8be10ef5dddd2ab36435f78a2b3fe7e32"
          "BLK-123-B01", "14a53a9d93427c57a049526ef41d6a687b1e471719f5ebce50a4a530c285dd42"
          "BLK-124-B01", "2563fcdb12c64f098f65a576d373733080ce174e05fb2d33da953bd7f5e289b2"
          "BLK-124-B02", "900199d97875110ccf0b2a7810ce9a09194c6fa8801717ae94dffe25aad00ad5"
          "BLK-125-B01", "a0edf91eda3af3dd19e8bc1f6965934b30726c8b30f205953537203f667c2e23"
          "BLK-126-B02", "d60a0e8f8055230055e578b41c7c524d1511086d01be36835ba1071495d1911e"
          "BLK-127-B02", "5428b23b675f7def114a10ef9b562ef6e63e3d21fc8306b738a2f3c1c0eb0034"
          "BLK-128-B01", "600e25c620c11cf175982a2b35331a6303fb8c729a01d271fec1b806a2f0b8b4"
          "BLK-128-B02", "1c50f13b2792b12fc30ca97444ce9eab46f1d6fb3f301718d90fb235dafd4420"
          "BLK-129-B01", "d0d81f3e7b40bf9c55c128cce8f224b06a7475a4fe0e618755eceaf92bd02911"
          "BLK-130-B01", "6ae439fde8f6c165ce77614166d59c3612741f4fe53c97c2bbc1e845d838c7fc"
          "BLK-130-T01", "c1f9c90a9284aa1d4303b376e30a1440bf0029b1254a5477bcc2f12b1e4b6270"
          "BLK-131-B01", "341cef061f25ea7c970c7eaf64b4e6763f91a0c36b21df764979fafcc84346d1"
          "BLK-132-T01", "88d9cb919427c5a8c51d3aff6823a18d18122202a59e9f147e5eb6b77194f068"
          "BLK-133-B01", "9ac1f59ca803d6e29208cb229a014b43b1929af2b5a93c14bb522a0c47d4006c"
          "BLK-134-B01", "2cfc600a04946dfcc90777ff87768ede7cb4e5ca230d4c54512e5da8b3578136"
          "BLK-134-B02", "0795a18cecfe03463da086cb25e606316b5f49d6f0a454c1cc4e162a5156fa83"
          "BLK-135-B02", "bf40e51e5e414517f4ee4120e69101e49ace611a7ce53facdfb00ccdd5601b0b"
          "BLK-136-B01", "276f353d9d0ff6d094770255b378354ac0a89a549a224e8334f5a2af58d15096"
          "BLK-136-B02", "81008e7415306c850d0604c7887e2b35e69b722a5ef554ee9b0b84b874c97b73"
          "BLK-138-B01", "98b55533ebbb1d3c8f41c3c00758eb061ad6895805542cc25ad2a90fbd68668c"
          "BLK-139-B01", "0e8e4d53c9395f3ccb1afb4db92f8ea840eae21233581c75657c67854e99e039"
          "BLK-139-T01", "0fe169aa6cab4637f3594d3c9b46392f46932f1d3fde576f4579827ed5b208f3"
          "BLK-140-B01", "4b8ffc38ddc0b01c04b8b7646e9ac46aeb8f327e0b9077fa9f45b2dc347bc550"
          "BLK-141-B01", "b2849984b0743e89d3617042b616b359b6d18e0f81921539965dfda3fab0843b"
          "BLK-141-T01", "80e158b0b7fb13fa16d208bb7609336763a756f524ceb1f86bf32831b133a26a"
          "BLK-142-B02", "648431d556dca19d902f5f2485c9ad0c43178813867cee68c109771c000b227d"
          "BLK-143-B01", "74396471c0a69198f3c925a4a417288eb7e5ee875be3f99c589fd652b361e77c"
          "BLK-143-T01", "a286f675057d6b2823dbc44a0535174f136ef5894a6b9181a56d0388dfffcf8c"
          "BLK-144-B01", "11ae003c8ac07d234fc4d326a48034030b5d59b7746719bd5ec02129bef2b8f4"
          "BLK-144-T01", "b2c08cbaa768fc447b50d910ef852ed6986a0bf5a8eb821bbf284e101a982850"
          "BLK-145-B01", "6b3353ef22341268dd15aa13a46305ce3e64b7493a0e4b7930ea39d2a7a0aa88"
          "BLK-145-T01", "2c89479a48ffd1dc52675978d2e7e4ee3d4ca58ab2649aa933ec1b38715dcfc2"
          "BLK-146-B01", "be7f6da8401ea5729d35423cbde81f1ad1d36629569a0040f6938b0925244549"
          "BLK-146-T01", "0b596fd2d72488aeebfcb50a71fe74cb1c9a0b8a00164e08bbedaec2867ea722"
          "BLK-148-B02", "38ce80b0125ce1567924edb4a914018b547180d9dd93db9bc8db56c8086ae0a7"
          "BLK-149-B01", "9e2f9559a552d28a0259f9946bd714929fb901977a8bf69e5d5a9d573460c4b7"
          "BLK-149-T01", "d01fb9b6787ce6688ce697fe5eba9ce9ae4686adfe0f2a91a97a3bdb30b959b2"
          "BLK-150-B01", "ae9e89230470cd0d23c35d2b924165e1b5dfa4ae285d7e2ca4ca115f6cda2910"
          "BLK-150-B02", "11ad20962d83e82b9887a8517e794b36c5b230caef5096ff843be5e6104de993"
          "BLK-151-T01", "815b54b85addde5728d6e2ce22be35aa55d2e2cc6cb09e11d97bded1bbd175d4"
          "KIT-001-R01", "f952d9ed24c3cfbd0437fdabb68349dee5b5c0126e77f192be9bef7f84665322"
          "KIT-001-T01", "0c4e775b565916afd1a374d63408e36d7e830fd88a45abcdd4a9657cea96f4ef"
          "KIT-002-R01", "86098c161f5720fced234dc38474f1ccf14534be88050498b90f1928c2af6451"
          "KIT-002-T01", "c0fd6aefc737e224df232c5d9d333b4b02e9dc3825293bf158cc9eda71e5d840"
          "KIT-003-R01", "cc391512b5684f8209243c8bd4b5786eef2564c28331b665003bb4f3c410189d"
          "KIT-003-T01", "8bdf1a403cf1f6c9d6a3f38b714f48e5cd5b8bfb60f1d503f094f44cbbc9ecff"
          "KIT-004-R01", "ed47267768a0ef3636f0598183579f296cdf1f1aa86672f7b311763a47d91da5"
          "KIT-005-R01", "3f89423eadc7690f22d4363d37dbb979caf9b47f0323f6e7b4321aef1b1337f5"
          "KIT-006-R01", "d6c963040af043b6ebaa54e6a936e236245ecf841d3220644925afb934caa7f8"
          "KIT-007-R01", "2629f6072c9bb5a6915779fb526f427649c32f8933c5afb3742d13b1b4891931"
          "KIT-008-R01", "d319f13459ab3d01d33753fc6ba341a0a548ba3073cd4d60796823c2b156e0f4"
          "KIT-009-R01", "1f6e7aa42cf63fed632eb8837fba1f9e1a57260797276514046a451d622b2032"
          "KIT-010-R01", "28c115d6f26e78d6a0bba409151833e1faf418e6e2a406ed5c7c9ab48c08be43"
          "KIT-011-R01", "61bf2d50b67dcbb88e1adb1fee7e402aabd8a93360fb42ce622b9c8f6d45942e"
          "KIT-012-R01", "f04bac03734bda6b045d8d5fa91f39ce9a9cdaef6841a2e6682cd6d1b6a9f92b" ]
        |> Map.ofList

type ProgramCompositionConformanceTests() =

    [<Test>]
    member _.``every executable recursive nontrivial program should preserve its MatchEngine semantic composition``
        ()
        =
        let executableIds =
            executableNontrivialPrograms
            |> Seq.map (fun (row, _) -> row.MechanicalId)
            |> Set.ofSeq

        expectedCompositionHashes.Count |> should equal 178
        expectedCompositionHashes |> Map.keys |> Set.ofSeq |> should equal executableIds

        let actual =
            executableNontrivialPrograms
            |> Array.map (fun (row, _) -> row.MechanicalId, compositionHash row)
            |> Map.ofArray

        actual |> should equal expectedCompositionHashes

    [<Test>]
    [<Explicit>]
    member _.``the composition snapshot generator should print every exact executable program row``
        ()
        =
        let lines = ResizeArray<string>()

        for row, _ in executableNontrivialPrograms do
            let line = $"          \"{row.MechanicalId}\", \"{compositionHash row}\""
            Console.WriteLine line
            lines.Add line

        match Environment.GetEnvironmentVariable "BLOKEMON_COMPOSITION_HASHES" with
        | null
        | "" -> ()
        | path -> File.WriteAllLines(path, lines)

        executableNontrivialPrograms.Length |> should equal 178
