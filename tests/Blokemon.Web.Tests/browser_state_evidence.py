#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
from pathlib import Path
import shutil
import tempfile

from headless_card_viewer import Chrome, HostedRoot, require


ROOT = Path(__file__).resolve().parents[2]
DEFAULT_MODULE = ROOT / "src/Blokemon.Web.Client/wwwroot/browserState.js"


def prepare_site(module: Path, destination: Path):
    (destination / "index.html").write_text("<!doctype html><title>Browser state evidence</title>")
    shutil.copy2(module, destination / "browserState.js")


def count_scenario(devtools):
    return devtools.evaluate(
        """
        (async () => {
          const realIndexedDB = globalThis.indexedDB;
          const openedDatabases = [];
          const counts = { opens: 0, transactions: 0 };
          Object.defineProperty(globalThis, "indexedDB", {
            configurable: true,
            value: {
              open(...arguments_) {
                counts.opens++;
                const request = realIndexedDB.open(...arguments_);
                request.addEventListener("success", () => openedDatabases.push(request.result));
                return request;
              }
            }
          });
          const originalTransaction = IDBDatabase.prototype.transaction;
          IDBDatabase.prototype.transaction = function (...arguments_) {
            const transaction = originalTransaction.apply(this, arguments_);
            counts.transactions++;
            return transaction;
          };

          const state = await import("/browserState.js?count-scenario");
          const created = await state.create("count-document", '{"step":1}');
          const duplicate = await state.create("count-document", '{"step":"duplicate"}');
          const firstRead = await state.read("count-document");
          const stale = await state.update("count-document", 0, '{"step":"stale"}');
          const afterStaleRead = await state.read("count-document");
          const updated = await state.update("count-document", 1, '{"step":2}');
          const secondRead = await state.read("count-document");
          const afterSequential = { ...counts };
          const concurrent = await Promise.all([
            state.read("count-document"),
            state.read("missing-one"),
            state.read("missing-two")
          ]);
          const afterConcurrent = { ...counts };
          await state.create("race-document", '{"winner":"none"}');
          const raceResults = await Promise.all([
            state.update("race-document", 1, '{"winner":"one"}'),
            state.update("race-document", 1, '{"winner":"two"}')
          ]);
          const raceRead = await state.read("race-document");
          const afterRace = { ...counts };
          await state.remove("count-document");
          const removed = await state.read("count-document");
          await state.remove("race-document");

          return {
            created,
            firstRead,
            duplicate,
            stale,
            afterStaleRead,
            updated,
            secondRead,
            concurrent,
            raceResults,
            raceRead,
            removed,
            afterSequential,
            concurrentDelta: {
              opens: afterConcurrent.opens - afterSequential.opens,
              transactions: afterConcurrent.transactions - afterSequential.transactions
            },
            raceDelta: {
              opens: afterRace.opens - afterConcurrent.opens,
              transactions: afterRace.transactions - afterConcurrent.transactions
            },
            total: { ...counts },
            openedConnections: openedDatabases.length
          };
        })()
        """
    )


def lifecycle_scenario(devtools):
    return devtools.evaluate(
        """
        (async () => {
          const realIndexedDB = globalThis.indexedDB;
          const openedDatabases = [];
          const counts = { openAttempts: 0, opens: 0, transactions: 0 };
          let failNextOpen = false;
          let failNextTransaction = false;

          Object.defineProperty(globalThis, "indexedDB", {
            configurable: true,
            value: {
              open(...arguments_) {
                counts.openAttempts++;
                if (failNextOpen) {
                  failNextOpen = false;
                  const request = {
                    error: new DOMException("Injected open failure", "UnknownError"),
                    onerror: null,
                    onupgradeneeded: null,
                    onsuccess: null
                  };
                  queueMicrotask(() => request.onerror?.(new Event("error")));
                  return request;
                }

                const request = realIndexedDB.open(...arguments_);
                request.addEventListener("success", () => {
                  counts.opens++;
                  openedDatabases.push(request.result);
                });
                return request;
              }
            }
          });

          const originalTransaction = IDBDatabase.prototype.transaction;
          IDBDatabase.prototype.transaction = function (...arguments_) {
            if (failNextTransaction) {
              failNextTransaction = false;
              throw new DOMException("Injected transaction failure", "InvalidStateError");
            }
            const transaction = originalTransaction.apply(this, arguments_);
            counts.transactions++;
            return transaction;
          };

          const state = await import("/browserState.js?lifecycle-scenario");
          await Promise.all([
            state.read("first-one"),
            state.read("first-two"),
            state.read("first-three")
          ]);
          const afterConcurrentFirstUse = { ...counts };

          const versionRequest = realIndexedDB.open("blokemon-browser-local-v1", 2);
          await new Promise((resolve, reject) => {
            versionRequest.onsuccess = resolve;
            versionRequest.onerror = reject;
          });
          versionRequest.result.close();
          await new Promise((resolve, reject) => {
            const request = realIndexedDB.deleteDatabase("blokemon-browser-local-v1");
            request.onsuccess = resolve;
            request.onerror = reject;
          });
          await state.create("lifecycle-document", '{"stage":"versionchange"}');
          const afterVersionChange = { ...counts };

          const closed = openedDatabases.at(-1);
          closed.close();
          closed.dispatchEvent(new Event("close"));
          const afterClose = await state.read("lifecycle-document");
          const afterClosure = { ...counts };

          globalThis.dispatchEvent(new PageTransitionEvent("pagehide"));
          const afterPageHide = await state.read("lifecycle-document");
          const afterDisposal = { ...counts };

          globalThis.dispatchEvent(new PageTransitionEvent("pagehide"));
          failNextOpen = true;
          let openFailure = null;
          try {
            await state.read("lifecycle-document");
          } catch (error) {
            openFailure = error.message;
          }
          const afterOpenFailure = { ...counts };
          const afterOpenRecovery = await state.read("lifecycle-document");
          const afterOpenReopen = { ...counts };

          failNextTransaction = true;
          let operationFailure = null;
          try {
            await state.read("lifecycle-document");
          } catch (error) {
            operationFailure = `${error.name}: ${error.message}`;
          }
          const afterOperationFailure = { ...counts };
          const afterOperationRecovery = await state.read("lifecycle-document");
          const afterOperationReopen = { ...counts };

          return {
            afterConcurrentFirstUse,
            afterVersionChange,
            afterClose,
            afterClosure,
            afterPageHide,
            afterDisposal,
            openFailure,
            afterOpenFailure,
            afterOpenRecovery,
            afterOpenReopen,
            operationFailure,
            afterOperationFailure,
            afterOperationRecovery,
            afterOperationReopen
          };
        })()
        """
    )


def verify_count_contract(result, expected_opens: int | None):
    require(result["created"] == 1, "create writes revision 1")
    require(result["firstRead"] == {"revision": 1, "json": '{"step":1}'}, "read returns the created document")
    require(result["duplicate"] is None, "duplicate create reports a conflict without replacing the row")
    require(result["stale"] is None, "stale update reports a conflict")
    require(result["afterStaleRead"] == result["firstRead"], "stale update leaves the current document unchanged")
    require(result["updated"] == 2, "valid update advances the revision")
    require(result["secondRead"] == {"revision": 2, "json": '{"step":2}'}, "update stores the replacement JSON")
    require(result["concurrent"] == [result["secondRead"], None, None], "concurrent reads retain exact read semantics")
    require(set(result["raceResults"]) == {None, 2}, "concurrent updates accept one writer and reject one stale writer")
    require(
        result["raceRead"]["revision"] == 2
        and result["raceRead"]["json"] in ('{"winner":"one"}', '{"winner":"two"}'),
        "the accepted concurrent update is the only stored replacement",
    )
    require(result["removed"] is None, "remove leaves the document absent")
    require(result["afterSequential"]["transactions"] == 7, "seven sequential operations create seven transactions")
    require(result["concurrentDelta"]["transactions"] == 3, "three concurrent reads create three transactions")
    require(result["raceDelta"]["transactions"] == 4, "the create, two atomic updates, and read create four transactions")
    require(result["total"]["transactions"] == 17, "seventeen document operations create seventeen transactions")
    if expected_opens is not None:
        require(
            result["total"]["opens"] == expected_opens,
            f"the count scenario uses {expected_opens} database open requests",
        )


def verify_lifecycle_contract(result):
    require(
        result["afterConcurrentFirstUse"] == {"openAttempts": 1, "opens": 1, "transactions": 3},
        "concurrent first use shares one successful database open",
    )
    require(
        result["afterVersionChange"] == {"openAttempts": 2, "opens": 2, "transactions": 4},
        "version change closes the old handle and the next operation reopens once",
    )
    require(
        result["afterClose"] == {"revision": 1, "json": '{"stage":"versionchange"}'},
        "close-event recovery preserves stored data",
    )
    require(
        result["afterClosure"] == {"openAttempts": 3, "opens": 3, "transactions": 5},
        "database closure invalidates the cached handle and reopens once",
    )
    require(result["afterPageHide"] == result["afterClose"], "page-hide disposal permits later reuse")
    require(
        result["afterDisposal"] == {"openAttempts": 4, "opens": 4, "transactions": 6},
        "page-hide disposal closes the shared handle before one reopen",
    )
    require(
        result["openFailure"] == "UnknownError: Injected open failure",
        "open failure retains the storage error name and message",
    )
    require(
        result["afterOpenFailure"] == {"openAttempts": 5, "opens": 4, "transactions": 6},
        "failed open creates no database handle or transaction",
    )
    require(result["afterOpenRecovery"] == result["afterClose"], "a later open recovers after an open failure")
    require(
        result["afterOpenReopen"] == {"openAttempts": 6, "opens": 5, "transactions": 7},
        "open failure clears the shared initialization before one bounded reopen",
    )
    require(
        result["operationFailure"].startswith("InvalidStateError:"),
        "operation failure retains its browser error classification",
    )
    require(
        result["afterOperationFailure"] == {"openAttempts": 6, "opens": 5, "transactions": 7},
        "failed transaction creation does not count as a created transaction",
    )
    require(
        result["afterOperationRecovery"] == result["afterClose"],
        "a later operation recovers after transaction creation fails",
    )
    require(
        result["afterOperationReopen"] == {"openAttempts": 7, "opens": 6, "transactions": 8},
        "operation failure invalidates the shared handle before one bounded reopen",
    )


def main():
    parser = argparse.ArgumentParser(description="Exercise browserState.js against Chromium IndexedDB")
    parser.add_argument("--module", type=Path, default=DEFAULT_MODULE)
    parser.add_argument("--counts-only", action="store_true")
    arguments = parser.parse_args()

    module = arguments.module.resolve()
    if not module.is_file():
        raise SystemExit(f"browser-state module not found: {module}")

    with tempfile.TemporaryDirectory(prefix="blokemon-browser-state-") as temporary:
        temporary_root = Path(temporary)
        site = temporary_root / "site"
        site.mkdir()
        prepare_site(module, site)
        with HostedRoot(site) as host:
            chrome = Chrome(temporary_root)
            try:
                chrome.devtools.command("Page.navigate", {"url": host.origin})
                chrome.devtools.wait_for("document.readyState === 'complete'", "browser-state evidence page")
                counts = count_scenario(chrome.devtools)
                print(f"COUNTS {json.dumps(counts, sort_keys=True)}")
                verify_count_contract(counts, None if arguments.counts_only else 1)
                if not arguments.counts_only:
                    chrome.devtools.command("Page.reload", {"ignoreCache": True})
                    chrome.devtools.wait_for("document.readyState === 'complete'", "fresh lifecycle evidence page")
                    lifecycle = lifecycle_scenario(chrome.devtools)
                    print(f"LIFECYCLE {json.dumps(lifecycle, sort_keys=True)}")
                    verify_lifecycle_contract(lifecycle)
            finally:
                chrome.close()


if __name__ == "__main__":
    main()
