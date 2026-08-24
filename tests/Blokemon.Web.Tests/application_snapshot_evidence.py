#!/usr/bin/env python3
from __future__ import annotations

import json
from pathlib import Path
import tempfile

from headless_card_viewer import Chrome, HostedRoot, published_root, require, run


def install_transaction_counter(devtools):
    devtools.command(
        "Page.addScriptToEvaluateOnNewDocument",
        {
            "source": """
              globalThis.__blokemonTransactions = 0;
              const transaction = IDBDatabase.prototype.transaction;
              IDBDatabase.prototype.transaction = function (...arguments_) {
                globalThis.__blokemonTransactions++;
                return transaction.apply(this, arguments_);
              };
            """
        },
    )


def transaction_count(devtools):
    return devtools.evaluate("globalThis.__blokemonTransactions")


def main():
    with tempfile.TemporaryDirectory(prefix="blokemon-application-snapshot-") as temporary:
        temporary_root = Path(temporary)
        output = temporary_root / "game"
        run(
            [
                "dotnet",
                "publish",
                "-m:1",
                "src/Blokemon.Web.Client/Blokemon.Web.Client.csproj",
                "--configuration",
                "Release",
                "--output",
                str(output),
                "-p:StandaloneBrowser=true",
                "-p:PublishTrimmed=false",
                "-p:BuildInParallel=false",
                "-p:TreatWarningsAsErrors=true",
            ]
        )

        with HostedRoot(published_root(output)) as game:
            chrome = Chrome(temporary_root)
            try:
                devtools = chrome.devtools
                install_transaction_counter(devtools)
                devtools.set_reduced_motion(True)
                devtools.navigate(game.origin, "/")
                devtools.wait_for(
                    "[...document.querySelectorAll('button')].some(button => button.textContent.trim() === 'Use this browser')",
                    "browser-local mode choice",
                )
                devtools.click_text("Use this browser")
                devtools.wait_for(
                    "document.querySelector('a[href=\"profile\"]') !== null",
                    "browser-local player setup",
                )

                devtools.command("Page.reload", {"ignoreCache": True})
                devtools.wait_for("document.readyState === 'complete'", "reloaded game document")
                devtools.wait_for(
                    "document.querySelector('a[href=\"profile\"]') !== null",
                    "coalesced browser-local application state",
                    timeout=30,
                )
                hydrated = transaction_count(devtools)

                devtools.click_text("Create player")
                devtools.wait_for("document.querySelector('#display-name') !== null", "profile form")
                before_mutation = transaction_count(devtools)
                devtools.set_value("#display-name", "Snapshot Player")
                devtools.click_text("Create player")
                devtools.wait_for(
                    "[...document.querySelectorAll('.starter-option button')].some(button => button.textContent.trim().startsWith('Open '))",
                    "mutation result rendered after navigation",
                    timeout=30,
                )
                after_mutation = transaction_count(devtools)

                devtools.click_text("Collection")
                devtools.wait_for(
                    "document.querySelector('.collection-grid') !== null",
                    "collection navigation from the mutation snapshot",
                )
                after_navigation = transaction_count(devtools)
                counts = {
                    "pageAndWarmupHydration": hydrated,
                    "createProfileMutation": after_mutation - before_mutation,
                    "navigationAfterMutation": after_navigation - after_mutation,
                }
                print(f"APPLICATION_SNAPSHOT {json.dumps(counts, sort_keys=True)}")
                require(
                    counts["pageAndWarmupHydration"] == 2,
                    "page plus warmup hydration reads settings and profile exactly once each",
                )
                require(
                    counts["createProfileMutation"] == 2,
                    "profile creation uses one write and one complete-view match read",
                )
                require(
                    counts["navigationAfterMutation"] == 0,
                    "navigation after mutation reuses the published application snapshot",
                )
            finally:
                chrome.close()

    print("Application snapshot browser evidence passed.")


if __name__ == "__main__":
    main()
