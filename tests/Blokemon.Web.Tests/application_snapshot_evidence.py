#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
from pathlib import Path
import subprocess
import tempfile

from headless_card_viewer import Chrome, HostedRoot, published_root, require


ROOT = Path(__file__).resolve().parents[2]


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
    parser = argparse.ArgumentParser(
        description="Measure application snapshot transactions in the checked-out game"
    )
    parser.add_argument("--source-root", type=Path, default=ROOT)
    parser.add_argument("--expected-hydration", type=int, default=2)
    parser.add_argument("--expected-mutation", type=int, default=2)
    parser.add_argument("--expected-navigation", type=int, default=0)
    arguments = parser.parse_args()
    source_root = arguments.source_root.resolve()
    if not (source_root / "src/Blokemon.Web.Client/Blokemon.Web.Client.csproj").is_file():
        raise SystemExit(f"Blokemon source root not found: {source_root}")

    with tempfile.TemporaryDirectory(prefix="blokemon-application-snapshot-") as temporary:
        temporary_root = Path(temporary)
        output = temporary_root / "game"
        command = [
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
        print(f"$ {' '.join(command)}", flush=True)
        subprocess.run(command, cwd=source_root, check=True)

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
                    counts["pageAndWarmupHydration"] == arguments.expected_hydration,
                    "page plus warmup hydration uses the expected transaction count",
                )
                require(
                    counts["createProfileMutation"] == arguments.expected_mutation,
                    "profile creation uses the expected transaction count",
                )
                require(
                    counts["navigationAfterMutation"] == arguments.expected_navigation,
                    "navigation after mutation uses the expected transaction count",
                )
            finally:
                chrome.close()

    print("Application snapshot browser evidence passed.")


if __name__ == "__main__":
    main()
