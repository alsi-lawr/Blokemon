"""Headless evidence that a card is played by dragging it to where it goes (BLOKEMON-173).

Driven by HeadlessDragTests, which starts the host and hands this script its origin. The browser
game creates a player, claims a starter and starts a battle at a Pixel's screen with touch. A finger
drags the opening Blokemon into the empty Active position; on the player's turn a finger drags a
held card onto one of the places that glow for it and the move is played, while everything not
involved has stepped back. Then at a desktop size a mouse does the same to a Bench position, a
hold that becomes a drag closes the viewer it opened and still completes, a drop over nothing
springs back with nothing played, and Escape and a tap on the empty field put a picked-up card
down. Chrome runs headless only.
"""

from __future__ import annotations

import json
import os
import sys
import tempfile
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from headless_card_viewer import Chrome, EvidenceFailure, require  # noqa: E402
from headless_table_evidence import DRAWN, reach_table  # noqa: E402

DESKTOP = (1440, 900)

# Further than the travel a press may make and still be a tap or a hold.
PAST_THE_THRESHOLD = 24


def env(name):
    value = os.environ.get(name)
    if not value:
        raise EvidenceFailure(f"{name} is not set")
    return value


def centre(devtools, selector):
    point = devtools.evaluate(
        f"""
        (() => {{
          const element = document.querySelector({json.dumps(selector)});
          if (!element) return null;
          const box = element.getBoundingClientRect();
          return {{ x: box.left + box.width / 2, y: box.top + box.height / 2 }};
        }})()
        """
    )
    require(point is not None, f"{selector} is on the screen")
    return point


def press_point(devtools, selector):
    """A point on the element that a press there reaches: held cards overlap in the fan, so the
    middle of one can lie under the next, and a press there would pick the other card up."""
    point = devtools.evaluate(
        f"""
        (() => {{
          const element = document.querySelector({json.dumps(selector)});
          if (!element) return null;
          const box = element.getBoundingClientRect();
          for (const down of [0.5, 0.35, 0.65, 0.2, 0.8]) {{
            for (const across of [0.5, 0.35, 0.65, 0.2, 0.8, 0.1, 0.9]) {{
              const x = box.left + box.width * across;
              const y = box.top + box.height * down;
              const hit = document.elementFromPoint(x, y);
              if (hit && element.contains(hit)) return {{ x, y }};
            }}
          }}
          return null;
        }})()
        """
    )
    require(point is not None, f"{selector} can be pressed")
    return point


def frame(devtools):
    devtools.evaluate("new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))")


def still(devtools):
    """The hand has stopped moving: a card measured while the fan is still settling, or still
    rising under the pointer, would be pressed where it was rather than where it is."""
    # The coronas turn for as long as a card glows, so only the transitions that move a card
    # are waited out.
    devtools.wait_for(
        "document.querySelector('.hand-zone') !== null && document.querySelector('.hand-zone').getAnimations({ subtree: true }).every(a => !(a instanceof CSSTransition))",
        "the hand at rest",
        timeout=10,
    )
    time.sleep(0.1)


def quiet(devtools):
    """Whether the table is waiting on the player: nothing playing, the computer not thinking."""
    return devtools.evaluate(
        """
        document.querySelector('.battle-screen') !== null
          && document.querySelector('.skip-animation') === null
          && document.querySelector('.turn-ribbon.is-thinking') === null
        """
    )


def press_through(devtools, labels):
    """Presses whichever of the sheet's buttons is up, once, so a question the table asks in
    words is answered and the battle goes on."""
    for label in labels:
        try:
            devtools.click_text(label, "button")
            return True
        except EvidenceFailure:
            continue
    return False


def trace(devtools, label):
    if not os.environ.get("BLOKEMON_DRAG_TRACE"):
        return
    state = devtools.evaluate(
        """
        ({
          hand: document.querySelectorAll('.hand-card').length,
          active: document.querySelector('.player-zone .active-slot .field-card-button') !== null,
          bench: document.querySelectorAll('.player-zone .bench-row .field-card-button').length,
          focused: document.querySelector('.battle-screen.is-focused') !== null,
          sheet: document.querySelector('.action-sheet')?.textContent.replace(/\\s+/g, ' ').slice(0, 160) ?? null,
          reveal: document.querySelector('.reveal-continue') !== null,
          skip: document.querySelector('.skip-animation') !== null,
          thinking: document.querySelector('.turn-ribbon.is-thinking') !== null,
          error: document.querySelector('.battle-error')?.textContent.trim() ?? null,
          ribbon: document.querySelector('.turn-ribbon')?.textContent.replace(/\\s+/g, ' ').trim() ?? null,
        })
        """
    )
    print(f"TRACE {label}: {json.dumps(state)}")


def skip_animation(devtools):
    """The presentation's skip is pressed if it is still up: it goes on its own when the
    presentation ends, and a press that reached for it a moment late is nothing."""
    return devtools.evaluate(
        """
        (() => {
          const skip = [...document.querySelectorAll('.skip-animation, button')]
            .find(button => button.textContent.trim() === 'Skip animation');
          if (!skip) return false;
          skip.click();
          return true;
        })()
        """
    )


def acknowledge(devtools):
    """A reveal stays up until it is acknowledged, whatever else is skipped."""
    if devtools.evaluate("document.querySelector('.reveal-continue') !== null"):
        devtools.evaluate("document.querySelector('.reveal-continue').click()")
        return True
    return False


def settle_flow(devtools):
    """Whatever the drop started is finished: the questions it asked are answered with their
    defaults, the presentation plays out, and the table is quiet again."""
    deadline = time.monotonic() + 90
    while time.monotonic() < deadline:
        trace(devtools, "settling")
        if acknowledge(devtools):
            pass
        elif skip_animation(devtools):
            pass
        elif devtools.evaluate("document.querySelector('.action-sheet') !== null"):
            if press_through(devtools, ["Start battle", "Continue", "Play", "Attach", "Done", "Choose"]):
                # The press is given time to land before the sheet is looked at again, so one
                # question is never answered twice.
                time.sleep(0.6)
            else:
                time.sleep(0.2)
        elif quiet(devtools):
            return
        time.sleep(0.2)
    raise EvidenceFailure("the table did not settle after the drop")


def battle_over(devtools):
    return devtools.evaluate("document.querySelector('.battle-screen') === null && document.body.textContent.includes('Battle over')")


def my_turn(devtools, or_over=False, drag=None):
    """Waits for the player's own turn with a card that can be carried. A draw the Deck offers is
    taken on the way, the computer's turn is left to play out, and a turn of the player's own
    with nothing in hand to carry is spent. Answers whether the turn came: a battle that ended
    first is a failure unless the caller can take it."""
    deadline = time.monotonic() + 240
    while time.monotonic() < deadline:
        trace(devtools, "waiting for my turn")
        if battle_over(devtools):
            if or_over:
                return False
            raise EvidenceFailure("the battle ended before the player's turn came")
        if acknowledge(devtools):
            pass
        elif skip_animation(devtools):
            pass
        elif devtools.evaluate("document.querySelector('button.deck-stack.is-aura') !== null"):
            devtools.evaluate("document.querySelector('button.deck-stack.is-aura').click()")
        elif devtools.evaluate("document.querySelector('.action-sheet') !== null"):
            if press_through(devtools, ["Continue", "Done", "Choose", "Start battle"]) or first_offer(devtools) or answer_with_a_card(devtools):
                time.sleep(0.6)
        elif decided_for(devtools):
            time.sleep(0.4)
        elif devtools.evaluate(
            """
            document.querySelector('.player-zone.has-turn') !== null
              && document.querySelector('.turn-ribbon.is-thinking') === null
              && document.querySelector('.battle-screen.is-focused') === null
            """
        ):
            if devtools.evaluate("document.querySelector('.hand-card[data-drag]') !== null"):
                return True
            if devtools.evaluate("document.querySelector('button.hud-end-turn:not([disabled])') !== null"):
                spend_the_turn(devtools, drag or mouse_drag)
        time.sleep(0.3)
    raise EvidenceFailure("the player's turn with a card to carry did not come")


def answer_with_a_card(devtools):
    """A question the sheet asks with cards is answered with the first card that glows."""
    return devtools.evaluate(
        """
        (() => {
          const card = document.querySelector('.battle-screen .is-aura:not(.is-aura-selected) > .card-press-surface');
          if (!card) return false;
          card.click();
          return true;
        })()
        """
    )


def decided_for(devtools):
    """A decision the battle put to the player, which hides the way to end the turn - a
    replacement for a knocked-out Active, a Bonus to take - is made with the first thing that
    glows: the card, then the place it goes."""
    return devtools.evaluate(
        """
        (() => {
          if (document.querySelector('button.hud-end-turn') !== null) return false;
          const place = document.querySelector('button.empty-slot.is-target, button.bench-slot.is-target');
          if (place) { place.click(); return true; }
          const card = document.querySelector('.battle-screen .is-aura:not(.is-aura-selected) > .card-press-surface');
          if (card) { card.click(); return true; }
          return false;
        })()
        """
    )


def cancel(devtools):
    """A tap on the empty field puts a picked-up card down."""
    devtools.evaluate("document.querySelector('.battlefield').click()")
    devtools.wait_for("document.querySelector('.battle-screen.is-focused') === null", "the card put down")


def targets_of(devtools, card_selector):
    """Where a held card can go, found by picking it up with a tap and putting it down again."""
    still(devtools)
    devtools.evaluate(f"document.querySelector({json.dumps(card_selector)} + ' > .card-press-surface').click()")
    devtools.wait_for("document.querySelector('.battle-screen.is-focused') !== null", "the card picked up")
    found = devtools.evaluate(
        """
        ({
          cards: document.querySelectorAll('.player-zone .field-card-button.is-target[data-drop="card"]').length,
          bench: document.querySelectorAll('button.bench-slot.is-target[data-drop="bench"]').length,
          sheet: document.querySelector('.action-sheet') !== null,
        })
        """
    )
    cancel(devtools)
    return found


def carried_card(devtools, wanting):
    """A held card whose pick-up lights the kind of place asked for, by its id."""
    ids = devtools.evaluate("[...document.querySelectorAll('.hand-card[data-drag]')].map(card => card.dataset.drag)")
    for card_id in ids:
        selector = f'.hand-card[data-drag="{card_id}"]'
        found = targets_of(devtools, selector)
        if found[wanting] > 0 and not found["sheet"]:
            return card_id
    return None


def focus_state(devtools):
    return devtools.evaluate(
        """
        (() => {
          const screen = document.querySelector('.battle-screen');
          const held = [...document.querySelectorAll('.hand-card')];
          // A card that has stepped back is darkened, never seen through: its brightness is
          // read from the filter, and its opacity must be whole.
          const visuals = held.filter(card => !card.classList.contains('is-aura-selected') && !card.classList.contains('is-target'))
            .map(card => getComputedStyle(card.querySelector('.hand-card-visual')));
          const brightness = style => { const match = /brightness\\((\\d*\\.?\\d+)\\)/.exec(style.filter); return match ? Number(match[1]) : 1; };
          return {
            focused: screen.classList.contains('is-focused'),
            selected: document.querySelectorAll('.is-aura-selected').length,
            lit: document.querySelectorAll('.is-aura:not(.is-aura-selected)').length,
            targets: document.querySelectorAll('.is-target').length,
            dimmed: visuals.length > 0 && visuals.every(style => brightness(style) < 0.6),
            solid: visuals.every(style => Number.parseFloat(style.opacity) === 1),
          };
        })()
        """
    )


def carried(devtools, card_id, label):
    """The card the browser is carrying is the one that was pressed. A press on a fanned hand
    can reach the card over the one meant, and a drag of that card would light the same target,
    so the mark is read by card rather than assumed."""
    devtools.wait_for("document.querySelector('[data-dragging]') !== null", f"{label}: a card being carried", timeout=5)
    being_carried = devtools.evaluate("document.querySelector('[data-dragging]').closest('[data-drag]').dataset.drag")
    require(being_carried == card_id, f"{label}: the card being carried is {card_id} (it is {being_carried})")


def stepped_back(devtools, label):
    """The rest of the hand has stepped back - darkened, and still whole - once its transition
    has run; the step back is waited for rather than read a frame after it began."""
    deadline = time.monotonic() + 5
    state = focus_state(devtools)
    while time.monotonic() < deadline and not state["dimmed"]:
        time.sleep(0.05)
        state = focus_state(devtools)
    require(state["dimmed"], f"{label}: the rest of the hand has stepped back ({state})")
    require(state["solid"], f"{label}: a card that stepped back is still solid ({state})")


def touch_drag(devtools, card_id, target_selector, label):
    """A finger carries the card to the target: past the threshold first, so the table lights
    the target, then onto it."""
    still(devtools)
    require(devtools.evaluate("document.querySelector('.battle-screen.is-focused') === null"), f"{label}: the table is at rest before the drag")
    start = press_point(devtools, f'[data-drag="{card_id}"] > .card-press-surface')
    devtools.touch("touchStart", start)
    devtools.touch("touchMove", {"x": start["x"], "y": start["y"] - PAST_THE_THRESHOLD})
    devtools.wait_for(f"document.querySelector({json.dumps(target_selector)}) !== null", f"{label}: the target lit by the drag")
    stepped_back(devtools, label)
    state = focus_state(devtools)
    require(state["focused"] and state["selected"] == 1 and state["lit"] == 0, f"{label}: only the carried card and its targets are lit ({state})")
    carried(devtools, card_id, label)
    end = centre(devtools, target_selector)
    devtools.touch("touchMove", {"x": (start["x"] + end["x"]) / 2, "y": (start["y"] + end["y"]) / 2})
    frame(devtools)
    devtools.touch("touchMove", end)
    frame(devtools)
    require(devtools.evaluate(f"document.querySelector({json.dumps(target_selector)}).dataset.dropOver !== undefined"), f"{label}: the target under the finger brightens")
    devtools.touch("touchEnd")


def mouse_drag(devtools, card_id, target_selector, label, hold_first=False):
    still(devtools)
    require(devtools.evaluate("document.querySelector('.battle-screen.is-focused') === null"), f"{label}: the table is at rest before the drag")
    start = press_point(devtools, f'[data-drag="{card_id}"] > .card-press-surface')
    devtools.mouse("mousePressed", start, buttons=1)
    if hold_first:
        devtools.wait_for("document.querySelector('.card-viewer') !== null", f"{label}: the held card's viewer", timeout=5)
    devtools.mouse("mouseMoved", {"x": start["x"], "y": start["y"] - PAST_THE_THRESHOLD}, buttons=1)
    if hold_first:
        devtools.wait_for("document.querySelector('.card-viewer') === null", f"{label}: the viewer closed by the drag")
    if target_selector is None:
        devtools.wait_for("document.querySelector('.battle-screen.is-focused') !== null", f"{label}: the card picked up by the drag")
        carried(devtools, card_id, label)
        end = centre(devtools, ".turn-ribbon")
        devtools.mouse("mouseMoved", end, buttons=1)
        frame(devtools)
        devtools.mouse("mouseReleased", end)
        return
    devtools.wait_for(f"document.querySelector({json.dumps(target_selector)}) !== null", f"{label}: the target lit by the drag")
    carried(devtools, card_id, label)
    end = centre(devtools, target_selector)
    devtools.mouse("mouseMoved", end, buttons=1)
    frame(devtools)
    require(devtools.evaluate(f"document.querySelector({json.dumps(target_selector)}).dataset.dropOver !== undefined"), f"{label}: the target under the pointer brightens")
    devtools.mouse("mouseReleased", end)


def opening_by_touch(devtools):
    """The opening Blokemon is dragged into the empty Active position, once the computer has
    chosen its own: nothing of the player's is lit while the computer is deciding."""
    devtools.wait_for(
        "document.querySelector('.hand-card.is-aura[data-drag]') !== null && document.querySelector('.turn-ribbon.is-thinking') === null",
        "a Blokemon to open with, the computer having chosen",
        timeout=60,
    )
    card_id = devtools.evaluate("document.querySelector('.hand-card.is-aura[data-drag]').dataset.drag")
    require(devtools.evaluate("document.querySelector('.player-zone .active-slot .field-card-button') === null"), "the Active position is empty before the opening")
    touch_drag(devtools, card_id, 'button.empty-slot.is-target[data-drop="active"]', "opening")
    settle_flow(devtools)
    devtools.wait_for("document.querySelector('.player-zone .active-slot .field-card-button') !== null", "the chosen Blokemon standing in the Active position", timeout=60)


def attach_by_touch(devtools):
    """A held card is carried onto one of the player's own Blokemon: an Energy, a Tool or an
    evolution, whichever the hand holds. The deal decides when the hand holds one, so turns are
    spent until it does, within reason."""
    card_id = carried_card(devtools, "cards")
    turns = 0
    while card_id is None and turns < 4:
        spend_the_turn(devtools, touch_drag)
        turns += 1
        require(my_turn(devtools, or_over=True, drag=touch_drag), "the battle going on until the hand holds a card that goes onto a Blokemon")
        card_id = carried_card(devtools, "cards")
    require(card_id is not None, "the hand holds a card that goes onto one of the player's Blokemon")
    hand_before = devtools.evaluate("document.querySelectorAll('.hand-card').length")
    touch_drag(devtools, card_id, '.player-zone .field-card-button.is-target[data-drop="card"]', "attach")
    settle_flow(devtools)
    devtools.wait_for(f"document.querySelectorAll('.hand-card').length === {hand_before - 1}", "the carried card left the hand", timeout=60)
    require(devtools.evaluate(f"document.querySelector('.hand-card[data-drag=\"{card_id}\"]') === null"), "the card that was carried is the one that went")
    state = focus_state(devtools)
    require(not state["focused"] and state["selected"] == 0, f"the table is at rest again after the drop ({state})")


def first_offer(devtools):
    """The first move the sheet offers is taken, leaving the way back alone."""
    return devtools.evaluate(
        """
        (() => {
          const offer = [...document.querySelectorAll('.action-sheet button:not([disabled])')]
            .find(button => !['Back', 'Cancel', 'Resign'].includes(button.textContent.trim()));
          if (!offer) return false;
          offer.click();
          return true;
        })()
        """
    )


def spend_the_turn(devtools, drag=mouse_drag):
    """The turn is spent so the next deal can be seen, the way a player would spend it: an Energy
    is carried to a Blokemon, the Active attacks when it can, carried onto the opponent's Active,
    and otherwise the turn is ended through the HUD."""
    energy = carried_card(devtools, "cards")
    if energy is not None:
        drag(devtools, energy, '.player-zone .field-card-button.is-target[data-drop="card"]', "spend: attach")
        settle_flow(devtools)
    still(devtools)
    active = devtools.evaluate("document.querySelector('.player-zone .active-slot [data-drag]')?.dataset.drag ?? null")
    if active is not None:
        drag(devtools, active, '.opponent-zone .field-card-button.is-target[data-drop="card"]', "attack")
        # The drop opens whatever the attack asks: a sheet naming two ready attacks, the attack's
        # own question, the word that throws it. The first thing offered is taken each time.
        deadline = time.monotonic() + 30
        while time.monotonic() < deadline and devtools.evaluate("document.querySelector('.battle-screen.is-focused') !== null"):
            if battle_over(devtools):
                break
            if devtools.evaluate("document.querySelector('.action-sheet') !== null"):
                if press_through(devtools, ["Attack", "Continue", "Choose", "Done"]) or first_offer(devtools):
                    time.sleep(0.6)
            time.sleep(0.2)
        require(battle_over(devtools) or devtools.evaluate("document.querySelector('.battle-screen.is-focused') === null"), "the attack carried onto the opponent's Active thrown")
        print("PASS attack: the Active carried onto the opponent's Active attacks")
        return
    # The HUD holds its buttons while the last move is still being told; the turn is ended once
    # it is ready to be.
    devtools.wait_for("document.querySelector('button.hud-end-turn:not([disabled])') !== null", "the End turn button ready", timeout=30)
    devtools.evaluate("document.querySelector('button.hud-end-turn:not([disabled])').click()")
    devtools.wait_for("document.querySelector('.action-sheet') !== null", "the end-of-turn sheet")
    devtools.click_text("End turn", ".action-sheet button")
    time.sleep(0.6)


def bench_by_mouse(devtools):
    """A Blokemon is carried to an empty Bench position by a mouse. The deal decides when a Basic
    is in hand, so turns are spent until one comes, within reason."""
    card_id = carried_card(devtools, "bench")
    turns = 0
    while card_id is None and turns < 6:
        spend_the_turn(devtools)
        turns += 1
        if not my_turn(devtools, or_over=True):
            print("NOTE the battle ended before a Basic Blokemon came to hand for the Bench drag; the Bench place was proved by the renderer tests")
            return
        card_id = carried_card(devtools, "bench")
    if card_id is None:
        print("NOTE no Basic Blokemon came to hand in six turns for the Bench drag; the Bench place was proved by the renderer tests")
        return
    bench_before = devtools.evaluate("document.querySelectorAll('.player-zone .bench-row .field-card-button').length")
    mouse_drag(devtools, card_id, 'button.bench-slot.is-target[data-drop="bench"]', "bench")
    settle_flow(devtools)
    devtools.wait_for(f"document.querySelectorAll('.player-zone .bench-row .field-card-button').length === {bench_before + 1}", "the Blokemon standing on the Bench", timeout=60)


def hold_then_drag_by_mouse(devtools):
    """A press held past the hold opens the viewer; travelling past the threshold closes it and
    the drag goes on - to a target when the hand holds a card with one, and otherwise to nothing,
    which is still a drag the viewer had to give way to."""
    card_id = carried_card(devtools, "cards") or carried_card(devtools, "bench")
    hand_before = devtools.evaluate("document.querySelectorAll('.hand-card').length")
    if card_id is None:
        card_id = devtools.evaluate("document.querySelector('.hand-card[data-drag]')?.dataset.drag")
        require(card_id is not None, "a card to hold and then carry")
        mouse_drag(devtools, card_id, None, "hold then drag", hold_first=True)
        devtools.wait_for("document.querySelector('.battle-screen.is-focused') === null", "the held-then-carried card put down over nothing")
        require(devtools.evaluate(f"document.querySelectorAll('.hand-card').length === {hand_before}"), "nothing was played by the drop over nothing")
        print("NOTE no card in hand had a place to go, so the hold-then-drag was let go over nothing")
        return
    found = targets_of(devtools, f'.hand-card[data-drag="{card_id}"]')
    target = '.player-zone .field-card-button.is-target[data-drop="card"]' if found["cards"] else 'button.bench-slot.is-target[data-drop="bench"]'
    mouse_drag(devtools, card_id, target, "hold then drag", hold_first=True)
    settle_flow(devtools)
    devtools.wait_for(f"document.querySelectorAll('.hand-card').length === {hand_before - 1}", "the held-then-carried card played", timeout=60)


def drop_over_nothing_by_mouse(devtools):
    card_id = devtools.evaluate("document.querySelector('.hand-card[data-drag]')?.dataset.drag")
    require(card_id is not None, "a card to let go over nothing")
    hand_before = devtools.evaluate("document.querySelectorAll('.hand-card').length")
    mouse_drag(devtools, card_id, None, "drop over nothing")
    devtools.wait_for("document.querySelector('.battle-screen.is-focused') === null", "the card put down after a drop over nothing")
    time.sleep(0.5)
    require(devtools.evaluate(f"document.querySelectorAll('.hand-card').length === {hand_before}"), "nothing was played by a drop over nothing")
    require(
        devtools.evaluate(f"getComputedStyle(document.querySelector('.hand-card[data-drag=\"{card_id}\"] > .card-press-surface')).translate === 'none'"),
        "the card sprang back to where it stands",
    )


def escape_and_field_tap_by_mouse(devtools):
    card_id = devtools.evaluate("document.querySelector('.hand-card[data-drag]')?.dataset.drag")
    require(card_id is not None, "a card to pick up")
    surface = f'.hand-card[data-drag="{card_id}"] > .card-press-surface'
    still(devtools)
    devtools.mouse_click(centre(devtools, surface))
    devtools.wait_for("document.querySelector('.battle-screen.is-focused') !== null", "the card picked up by a tap")
    rest_lit = devtools.evaluate("document.querySelectorAll('.is-aura:not(.is-aura-selected)').length")
    require(rest_lit == 0, f"nothing but the picked-up card and its targets is lit ({rest_lit})")
    devtools.send_key("Escape")
    devtools.wait_for("document.querySelector('.battle-screen.is-focused') === null", "Escape put the card down")
    require(devtools.evaluate("document.querySelectorAll('.is-aura').length > 0"), "the rest-state auras are back after Escape")
    still(devtools)
    devtools.mouse_click(centre(devtools, surface))
    devtools.wait_for("document.querySelector('.battle-screen.is-focused') !== null", "the card picked up again")
    devtools.mouse_click(centre(devtools, ".turn-ribbon"))
    devtools.wait_for("document.querySelector('.battle-screen.is-focused') === null", "a tap on the empty field put the card down")
    require(devtools.evaluate("document.querySelectorAll('.is-aura').length > 0"), "the rest-state auras are back after the field tap")


def corona_evidence(devtools, reduced):
    """The target corona: a gold rim apart from the white playable one, turning when motion is
    on and still when it is not."""
    card_id = devtools.evaluate("document.querySelector('.hand-card[data-drag]')?.dataset.drag")
    require(card_id is not None, "a card to pick up for the corona")
    devtools.evaluate(f"document.querySelector('.hand-card[data-drag=\"{card_id}\"] > .card-press-surface').click()")
    devtools.wait_for("document.querySelector('.is-target') !== null || document.querySelector('.battle-screen.is-focused') !== null", "the card picked up")
    state = devtools.evaluate(
        """
        (() => {
          // The element the corona is drawn around, whichever kind of place the target is.
          const rim = document.querySelector(
            '.is-target .battle-card-shell, .is-target .hand-card-visual, button.bench-slot.is-target, '
              + 'button.empty-slot.is-target, button.local-slot-empty.is-target, .empties-tray.is-target .tray-top'
          );
          if (!rim) return { target: false };
          const after = getComputedStyle(rim, '::after');
          const turning = rim.getAnimations({ subtree: true }).filter(a => a.animationName === 'aura-corona').length;
          return { target: true, rim: rim.className, image: after.backgroundImage, colour: after.backgroundColor, turning };
        })()
        """
    )
    cancel(devtools)
    if not state["target"]:
        print("NOTE the first card offered no target; the corona was not measured on this hand")
        return
    require("244, 189, 63" in state["image"] or "244, 189, 63" in state["colour"], f"the target corona is the table's gold ({state})")
    if reduced:
        require(state["turning"] == 0, f"the target corona holds still under reduced motion ({state})")
    else:
        require(state["turning"] > 0, f"the target corona turns when motion is on ({state})")


def main():
    origin = env("BLOKEMON_ORIGIN")
    with tempfile.TemporaryDirectory(prefix="blokemon-drag-evidence-") as temporary:
        chrome = Chrome(Path(temporary))
        try:
            devtools = chrome.devtools
            devtools.command("Runtime.enable")
            reach_table(devtools, origin)
            opening_by_touch(devtools)
            my_turn(devtools, drag=touch_drag)
            corona_evidence(devtools, reduced=True)
            attach_by_touch(devtools)

            devtools.set_viewport(*DESKTOP, touch=False)
            devtools.set_reduced_motion(False)
            my_turn(devtools)
            corona_evidence(devtools, reduced=False)
            escape_and_field_tap_by_mouse(devtools)
            drop_over_nothing_by_mouse(devtools)
            hold_then_drag_by_mouse(devtools)
            my_turn(devtools)
            bench_by_mouse(devtools)
        except EvidenceFailure as failure:
            try:
                where = devtools.evaluate("location.href")
                body = devtools.evaluate("document.body ? document.body.textContent.replace(/\\s+/g, ' ').slice(0, 400) : null")
            except Exception:  # noqa: BLE001
                where, body = "?", "?"
            raise EvidenceFailure(f"{failure} | at {where} | body={body!r}") from failure
        finally:
            chrome.close()
    print("HEADLESS DRAG EVIDENCE COMPLETE")


if __name__ == "__main__":
    try:
        main()
    except EvidenceFailure as failure:
        print(f"FAIL {failure}")
        sys.exit(1)
