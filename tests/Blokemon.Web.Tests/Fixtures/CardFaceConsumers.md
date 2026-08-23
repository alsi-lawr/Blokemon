# CardFace direct-consumer inventory

BLOKEMON-121 generated this inventory from the complete Razor source list at its ticket commit. Regenerate the ordered
source list with:

```sh
LC_ALL=C rg --sort path -n --glob '*.razor' '<CardFace\b' src/Blokemon.Web.Client
```

The command reports 21 direct consumers. The classification below is ordered by path and source occurrence. `Routed`
means the face reaches the app-level `CardViewerHost` through `CardPress` or the attachment's established `CardHold`.
A primary card action and its reading control are sibling controls inside `CardPress`, never interactive descendants.

1. `Components/AttachedCard.razor:24` — **routed**. Pointer hold uses the shared host directly; the containing
   `CardPress` supplies the sibling keyboard/screen-reader control for the attached identity.
2. `Components/BattleCard.razor:2` — **routed**. Every production `BattleCard` is the child of the action-bearing
   `CardPress` for its in-play, Active, or Bench instance.
3. `Components/CardViewer.razor:24` — **viewer self-face**. The canonical enlarged face is intentionally not pressable.
4. `Components/MatchActionDock.razor:22` — **routed**. The passive selected-card preview uses a reading-only
   `CardPress`; attack and play actions remain separate siblings.
5. `Components/MatchActionSheet.razor:97` — **routed**. The choice card's short activation selects it, while hold or
   its sibling reading control only reads it.
6. `Components/MatchCueOverlays.razor:17` — **transitory duplicate delegated to a named stable source**. A travelling
   play card is decorative and interaction-free; its readable source is `MatchHandZone` before travel and its readable
   `BattleCard` or `MatchEmptiesViewer` destination after travel.
7. `Components/MatchCueOverlays.razor:27` — **routed**. A non-travelling presentation card has no visible travelling
   source, so its reading-only `CardPress` opens the shared viewer.
8. `Components/MatchCueOverlays.razor:72` — **routed**. Each revealed cue card is read without bubbling to the cue's
   acknowledgement surface.
9. `Components/MatchEmptiesViewer.razor:34` — **routed**. Each stable tray card is reading-only and passes its
   `CardView` directly, without entering match selection lookup.
10. `Components/MatchHandZone.razor:25` — **routed**. The action-bearing hand `CardPress` preserves tap selection and
    hold behavior; the temporary deal back remains a separate cover.
11. `Components/MatchSide.razor:65` — **routed**. The top Empties face shares one `CardPress` with the tray-open action;
    its sibling reading control reads without opening the tray.
12. `Pages/Collection.razor:37` — **routed**. The collection tile is a reading-only `CardPress`.
13. `Pages/Decks.razor:90` — **routed**. The claimed-starter leader is a reading-only `CardPress`.
14. `Pages/Decks.razor:148` — **routed**. The catalogue face reads independently of the sibling quantity stepper.
15. `Pages/Decks.razor:178` — **routed**. The deck-list face is a reading-only `CardPress`.
16. `Pages/Home.razor:161` — **routed**. Each recently pulled face is a reading-only `CardPress`.
17. `Pages/Home.razor:194` — **routed**. Each starter-opening summary face is a reading-only `CardPress`.
18. `Pages/Home.razor:268` — **routed**. The starter leader reads independently of its sibling claim action.
19. `Pages/Packs.razor:67` — **routed**. Each Last Pack face is a reading-only `CardPress`.
20. `Pages/Packs.razor:95` — **routed**. Each opening-summary face is a reading-only `CardPress`.
21. `Pages/Packs.razor:156` — **hidden/non-readable before reveal; routed after reveal**. The face remains decorative
    behind `CardBack`, and its containing `CardPress` has no reading identity or hold route until `_cardFaceUp`; once
    face-up, its sibling reader opens the viewer without invoking flip/advance.

No JavaScript or non-Razor code creates `CardFace`. Hidden Deck, opponent-hand, Prize, draw-cover, opening-stack, and
flip-back pieces use `CardBack`, not a face consumer.
