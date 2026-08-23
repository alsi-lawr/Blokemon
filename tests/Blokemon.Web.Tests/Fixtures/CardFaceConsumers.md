# CardFace direct-consumer inventory

This inventory classifies the complete Razor source list for the shared card viewer. The executable headless check
derives the same ordered `path#occurrence` keys from the checkout and fails if this list is incomplete or out of order.
To inspect the source occurrences directly, run:

```sh
LC_ALL=C rg --sort path -n --glob '*.razor' '<CardFace\b' src/Blokemon.Web.Client
```

`Routed` means the face reaches the app-level `CardViewerHost` through `CardPress` or the attachment's established
`CardHold`. A primary card action and its reading control are sibling controls inside `CardPress`, never interactive
descendants.

1. `Components/AttachedCard.razor#1` — **Routed.** Pointer hold uses the shared host directly; the containing
   `CardPress` supplies the sibling keyboard and screen-reader control for the attached identity.
2. `Components/BattleCard.razor#1` — **Routed.** Every production `BattleCard` is the child of the action-bearing
   `CardPress` for its in-play, Active, or Bench instance.
3. `Components/CardViewer.razor#1` — **Viewer self-face.** The canonical enlarged face is intentionally not pressable.
4. `Components/MatchActionDock.razor#1` — **Routed.** The passive selected-card preview uses a reading-only
   `CardPress`; attack and play actions remain separate siblings.
5. `Components/MatchActionSheet.razor#1` — **Routed.** The choice card's short activation selects it, while hold or its
   sibling reading control only reads it.
6. `Components/MatchCueOverlays.razor#1` — **Transitory duplicate delegated to a named stable source.** A travelling
   play card is decorative and interaction-free; its readable source is `MatchHandZone` before travel and its readable
   `BattleCard` or `MatchEmptiesViewer` destination after travel.
7. `Components/MatchCueOverlays.razor#2` — **Routed.** A non-travelling presentation card has no visible travelling
   source, so its reading-only `CardPress` opens the shared viewer.
8. `Components/MatchCueOverlays.razor#3` — **Routed.** Each revealed cue card is read without bubbling to the cue's
   acknowledgement surface.
9. `Components/MatchEmptiesViewer.razor#1` — **Routed.** Each stable tray card is reading-only and passes its `CardView`
   directly, without entering match selection lookup.
10. `Components/MatchHandZone.razor#1` — **Routed.** The action-bearing hand `CardPress` preserves tap selection and
    hold behavior; the temporary deal back remains a separate cover.
11. `Components/MatchSide.razor#1` — **Routed.** The top Empties face shares one `CardPress` with the tray-open action;
    its sibling reading control reads without opening the tray.
12. `Pages/Collection.razor#1` — **Routed.** The collection tile is a reading-only `CardPress`.
13. `Pages/Decks.razor#1` — **Routed.** The claimed-starter leader is a reading-only `CardPress`.
14. `Pages/Decks.razor#2` — **Routed.** The catalogue face reads independently of the sibling quantity stepper.
15. `Pages/Decks.razor#3` — **Routed.** The deck-list face is a reading-only `CardPress`.
16. `Pages/Home.razor#1` — **Routed.** Each recently pulled face is a reading-only `CardPress`.
17. `Pages/Home.razor#2` — **Routed.** Each starter-opening summary face is a reading-only `CardPress`.
18. `Pages/Home.razor#3` — **Routed.** The starter leader reads independently of its sibling claim action.
19. `Pages/Packs.razor#1` — **Routed.** Each Last Pack face is a reading-only `CardPress`.
20. `Pages/Packs.razor#2` — **Routed.** Each opening-summary face is a reading-only `CardPress`.
21. `Pages/Packs.razor#3` — **Hidden or non-readable before reveal; routed after reveal.** The face remains decorative
    behind `CardBack`, and its containing `CardPress` has no reading identity or hold route until face-up; once face-up,
    its sibling reader opens the viewer without invoking flip or advance.

No JavaScript or non-Razor code creates `CardFace`. Hidden Deck, opponent-hand, Prize, draw-cover, opening-stack, and
flip-back pieces use `CardBack`, not a face consumer.
