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

          const prepare = realIndexedDB.open("blokemon-browser-local-v1", 1);
          prepare.onupgradeneeded = () => {
            if (!prepare.result.objectStoreNames.contains("documents")) {
              prepare.result.createObjectStore("documents", { keyPath: "key" });
            }
          };
          const prepared = await new Promise((resolve, reject) => {
            prepare.onsuccess = () => resolve(prepare.result);
            prepare.onerror = () => reject(prepare.error);
          });
          const seed = prepared.transaction("documents", "readwrite");
          seed.objectStore("documents").put({
            key: "cold-document",
            revision: 1,
            json: '{"step":"seeded"}'
          });
          await new Promise((resolve, reject) => {
            seed.oncomplete = resolve;
            seed.onabort = reject;
            seed.onerror = reject;
          });
          prepared.close();

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
          const beforeColdExisting = { ...counts };
          const coldExisting = await state.read("cold-document");
          const afterColdExisting = { ...counts };
          const hotExisting = await state.read("cold-document");
          const afterHotExisting = { ...counts };

          const coldMissing = await state.read("missing-document");
          const afterColdMissing = { ...counts };
          const hotMissing = await state.read("missing-document");
          const afterHotMissing = { ...counts };

          const created = await state.create("created-document", '{"step":1}');
          const createdRead = await state.read("created-document");
          const afterCreateAndRead = { ...counts };
          const duplicate = await state.create("created-document", '{"step":"duplicate"}');
          const duplicateReload = await state.read("created-document");
          const duplicateHotRead = await state.read("created-document");
          const afterCreateConflict = { ...counts };

          const actionRead = await state.read("cold-document");
          const updated = await state.update(
            "cold-document",
            actionRead.revision,
            '{"step":"updated"}'
          );
          const actionResultRead = await state.read("cold-document");
          const afterAction = { ...counts };

          await state.remove("created-document");
          const removedRead = await state.read("created-document");
          const afterDeleteAndRead = { ...counts };

          const stale = await state.update("cold-document", 1, '{"step":"stale"}');
          const conflictReload = await state.read("cold-document");
          const conflictHotRead = await state.read("cold-document");
          const afterConflict = { ...counts };

          return {
            coldExisting,
            hotExisting,
            coldMissing,
            hotMissing,
            created,
            createdRead,
            duplicate,
            duplicateReload,
            duplicateHotRead,
            actionRead,
            updated,
            actionResultRead,
            removedRead,
            stale,
            conflictReload,
            conflictHotRead,
            deltas: {
              coldExisting: afterColdExisting.transactions - beforeColdExisting.transactions,
              hotExisting: afterHotExisting.transactions - afterColdExisting.transactions,
              coldMissing: afterColdMissing.transactions - afterHotExisting.transactions,
              hotMissing: afterHotMissing.transactions - afterColdMissing.transactions,
              createAndRead: afterCreateAndRead.transactions - afterHotMissing.transactions,
              createConflictAndReload:
                afterCreateConflict.transactions - afterCreateAndRead.transactions,
              action: afterAction.transactions - afterCreateConflict.transactions,
              deleteAndRead: afterDeleteAndRead.transactions - afterAction.transactions,
              conflictAndReload: afterConflict.transactions - afterDeleteAndRead.transactions
            },
            total: { ...counts }
          };
        })()
        """
    )


def invalidation_scenario(devtools):
    return devtools.evaluate(
        """
        (async () => {
          const counts = { transactions: 0 };
          const originalTransaction = IDBDatabase.prototype.transaction;
          IDBDatabase.prototype.transaction = function (...arguments_) {
            const transaction = originalTransaction.apply(this, arguments_);
            counts.transactions++;
            return transaction;
          };

          const first = await import("/browserState.js?invalidation-first");
          const second = await import("/browserState.js?invalidation-second");
          await first.create("broadcast-affected", '{"owner":"first"}');
          await first.create("broadcast-unrelated", '{"owner":"first"}');
          const firstAffected = await first.read("broadcast-affected");
          const firstUnrelated = await first.read("broadcast-unrelated");
          const secondAffected = await second.read("broadcast-affected");
          const secondUpdate = await second.update(
            "broadcast-affected",
            secondAffected.revision,
            '{"owner":"second"}'
          );

          await new Promise((resolve) => setTimeout(resolve, 50));
          const beforeUnrelatedRead = counts.transactions;
          const unrelatedAfterSignal = await first.read("broadcast-unrelated");
          const afterUnrelatedRead = counts.transactions;
          const affectedAfterSignal = await first.read("broadcast-affected");
          const afterAffectedRead = counts.transactions;

          return {
            firstAffected,
            firstUnrelated,
            secondUpdate,
            unrelatedAfterSignal,
            affectedAfterSignal,
            unrelatedReadTransactions: afterUnrelatedRead - beforeUnrelatedRead,
            affectedReadTransactions: afterAffectedRead - afterUnrelatedRead,
            total: { ...counts }
          };
        })()
        """
    )


def conflict_without_signal_scenario(devtools):
    return devtools.evaluate(
        """
        (async () => {
          const counts = { transactions: 0 };
          const originalTransaction = IDBDatabase.prototype.transaction;
          IDBDatabase.prototype.transaction = function (...arguments_) {
            const transaction = originalTransaction.apply(this, arguments_);
            counts.transactions++;
            return transaction;
          };

          async function staleWriter(name) {
            const oldTab = await import(`/browserState.js?${name}-old-tab`);
            const currentTab = await import(`/browserState.js?${name}-current-tab`);
            const key = `${name}-document`;
            await oldTab.create(key, '{"owner":"old"}');
            const oldValue = await oldTab.read(key);
            const currentValue = await currentTab.read(key);
            await currentTab.update(key, currentValue.revision, '{"owner":"current"}');

            const staleHotRead = await oldTab.read(key);
            const beforeConflict = counts.transactions;
            const conflict = await oldTab.update(
              key,
              staleHotRead.revision,
              '{"owner":"stale"}'
            );
            const reloaded = await oldTab.read(key);
            const hotReload = await oldTab.read(key);
            return {
              oldValue,
              staleHotRead,
              conflict,
              reloaded,
              hotReload,
              conflictAndReloadTransactions: counts.transactions - beforeConflict
            };
          }

          const NativeBroadcastChannel = globalThis.BroadcastChannel;
          const delayedMessages = [];
          class DelayedBroadcastChannel {
            constructor(name) {
              this.channel = new NativeBroadcastChannel(name);
              this.channel.onmessage = (event) => this.onmessage?.(event);
            }

            postMessage(message) {
              delayedMessages.push(() => this.channel.postMessage(message));
            }

            close() {
              this.channel.close();
            }
          }
          Object.defineProperty(globalThis, "BroadcastChannel", {
            configurable: true,
            value: DelayedBroadcastChannel
          });
          const delayed = await staleWriter("delayed");

          Object.defineProperty(globalThis, "BroadcastChannel", {
            configurable: true,
            value: undefined
          });
          const absent = await staleWriter("absent");

          return {
            delayed,
            absent,
            delayedMessages: delayedMessages.length,
            total: { ...counts }
          };
        })()
        """
    )


def read_write_race_scenario(devtools):
    return devtools.evaluate(
        """
        (async () => {
          const realIndexedDB = globalThis.indexedDB;
          const prepare = realIndexedDB.open("blokemon-browser-local-v1", 1);
          const prepared = await new Promise((resolve, reject) => {
            prepare.onsuccess = () => resolve(prepare.result);
            prepare.onerror = () => reject(prepare.error);
          });
          const seed = prepared.transaction("documents", "readwrite");
          seed.objectStore("documents").put({
            key: "read-write-race",
            revision: 1,
            json: '{"stage":"before"}'
          });
          await new Promise((resolve, reject) => {
            seed.oncomplete = resolve;
            seed.onabort = reject;
            seed.onerror = reject;
          });
          prepared.close();

          let delayNextReadCompletion = true;
          let releaseReadCompletion;
          let readTransactionCompleted;
          const readTransactionDone = new Promise((resolve) => {
            readTransactionCompleted = resolve;
          });
          const counts = { transactions: 0 };
          const originalTransaction = IDBDatabase.prototype.transaction;
          IDBDatabase.prototype.transaction = function (...arguments_) {
            const transaction = originalTransaction.apply(this, arguments_);
            counts.transactions++;
            if (delayNextReadCompletion && arguments_[1] === "readonly") {
              delayNextReadCompletion = false;
              transaction.addEventListener("complete", readTransactionCompleted);
              Object.defineProperty(transaction, "oncomplete", {
                configurable: true,
                set(handler) {
                  releaseReadCompletion = handler;
                }
              });
            }
            return transaction;
          };

          const state = await import("/browserState.js?read-write-race");
          const staleReadPromise = state.read("read-write-race");
          await readTransactionDone;
          const updated = await state.update("read-write-race", 1, '{"stage":"after"}');
          releaseReadCompletion();
          const staleRead = await staleReadPromise;
          const beforeFinalRead = counts.transactions;
          const finalRead = await state.read("read-write-race");
          const afterFinalRead = counts.transactions;

          return {
            staleRead,
            updated,
            finalRead,
            finalReadTransactions: afterFinalRead - beforeFinalRead,
            total: { ...counts }
          };
        })()
        """
    )


def failure_scenario(devtools):
    return devtools.evaluate(
        """
        (async () => {
          let failNextTransaction = false;
          let abortNextWrite = false;
          const counts = { transactions: 0 };
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

          for (const method of ["add", "put", "delete"]) {
            const original = IDBObjectStore.prototype[method];
            IDBObjectStore.prototype[method] = function (...arguments_) {
              const request = original.apply(this, arguments_);
              if (abortNextWrite) {
                abortNextWrite = false;
                request.addEventListener("success", () => request.transaction.abort());
              }
              return request;
            };
          }

          const state = await import("/browserState.js?failure-scenario");
          await state.create("failure-update", '{"stage":"committed"}');
          const committedUpdate = await state.read("failure-update");

          failNextTransaction = true;
          let transactionFailure = null;
          try {
            await state.update("failure-update", 1, '{"stage":"failed"}');
          } catch (error) {
            transactionFailure = `${error.name}: ${error.message}`;
          }
          const beforeFailureReload = counts.transactions;
          const afterTransactionFailure = await state.read("failure-update");
          const transactionFailureReloads = counts.transactions - beforeFailureReload;

          abortNextWrite = true;
          let updateAbort = null;
          try {
            await state.update("failure-update", 1, '{"stage":"aborted"}');
          } catch (error) {
            updateAbort = `${error.name}: ${error.message}`;
          }
          const beforeUpdateAbortReload = counts.transactions;
          const afterUpdateAbort = await state.read("failure-update");
          const updateAbortReloads = counts.transactions - beforeUpdateAbortReload;

          await state.read("failure-create");
          abortNextWrite = true;
          let createAbort = null;
          try {
            await state.create("failure-create", '{"stage":"aborted"}');
          } catch (error) {
            createAbort = `${error.name}: ${error.message}`;
          }
          const beforeCreateAbortReload = counts.transactions;
          const afterCreateAbort = await state.read("failure-create");
          const createAbortReloads = counts.transactions - beforeCreateAbortReload;

          await state.create("failure-delete", '{"stage":"committed"}');
          abortNextWrite = true;
          let deleteAbort = null;
          try {
            await state.remove("failure-delete");
          } catch (error) {
            deleteAbort = `${error.name}: ${error.message}`;
          }
          const beforeDeleteAbortReload = counts.transactions;
          const afterDeleteAbort = await state.read("failure-delete");
          const deleteAbortReloads = counts.transactions - beforeDeleteAbortReload;

          return {
            committedUpdate,
            transactionFailure,
            afterTransactionFailure,
            transactionFailureReloads,
            updateAbort,
            afterUpdateAbort,
            updateAbortReloads,
            createAbort,
            afterCreateAbort,
            createAbortReloads,
            deleteAbort,
            afterDeleteAbort,
            deleteAbortReloads,
            total: { ...counts }
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
          const afterClose = await state.read("after-close");
          const afterClosure = { ...counts };

          globalThis.dispatchEvent(new PageTransitionEvent("pagehide"));
          const afterPageHide = await state.read("lifecycle-document");
          const afterDisposal = { ...counts };

          globalThis.dispatchEvent(new PageTransitionEvent("pagehide"));
          failNextOpen = true;
          let openFailure = null;
          try {
            await state.read("after-open-failure");
          } catch (error) {
            openFailure = error.message;
          }
          const afterOpenFailure = { ...counts };
          const afterOpenRecovery = await state.read("after-open-failure");
          const afterOpenReopen = { ...counts };

          failNextTransaction = true;
          let operationFailure = null;
          try {
            await state.read("after-operation-failure");
          } catch (error) {
            operationFailure = `${error.name}: ${error.message}`;
          }
          const afterOperationFailure = { ...counts };
          const afterOperationRecovery = await state.read("after-operation-failure");
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
    seeded = {"revision": 1, "json": '{"step":"seeded"}'}
    updated = {"revision": 2, "json": '{"step":"updated"}'}
    require(result["coldExisting"] == seeded, "a cold existing key hydrates its exact document")
    require(result["hotExisting"] == seeded, "a hot existing key retains its exact document")
    require(result["coldMissing"] is None, "a cold missing key hydrates absence")
    require(result["hotMissing"] is None, "a hot missing key retains cached absence")
    require(result["created"] == 1, "create writes revision 1")
    require(
        result["createdRead"] == {"revision": 1, "json": '{"step":1}'},
        "a successful create writes through its document",
    )
    require(result["duplicate"] is None, "a duplicate create reports a conflict")
    require(result["duplicateReload"] == result["createdRead"], "a create conflict reloads the durable row")
    require(result["duplicateHotRead"] == result["createdRead"], "the create-conflict reload becomes hot")
    require(result["actionRead"] == seeded, "the action-shaped read uses the current document")
    require(result["updated"] == 2, "the action-shaped update advances the revision")
    require(result["actionResultRead"] == updated, "a successful update writes through its document")
    require(result["removedRead"] is None, "a successful delete writes through cached absence")
    require(result["stale"] is None, "a stale update reports a conflict")
    require(result["conflictReload"] == updated, "a conflict reloads the durable document once")
    require(result["conflictHotRead"] == updated, "the conflict reload becomes hot")
    require(result["deltas"]["coldExisting"] == 1, "a cold existing key uses one transaction")
    require(result["deltas"]["hotExisting"] == 0, "a hot existing key uses no transaction")
    require(result["deltas"]["coldMissing"] == 1, "a cold missing key uses one transaction")
    require(result["deltas"]["hotMissing"] == 0, "a hot missing key uses no transaction")
    require(result["deltas"]["createAndRead"] == 1, "create plus its hot read uses one transaction")
    require(
        result["deltas"]["createConflictAndReload"] == 2,
        "a create conflict uses one write transaction and one bounded reload",
    )
    require(result["deltas"]["action"] == 1, "a hot read plus update plus hot read uses one transaction")
    require(result["deltas"]["deleteAndRead"] == 1, "delete plus its hot read uses one transaction")
    require(
        result["deltas"]["conflictAndReload"] == 2,
        "a conflict uses one update transaction and one bounded reload",
    )
    require(result["total"]["transactions"] == 9, "the complete cache contract uses nine transactions")
    if expected_opens is not None:
        require(
            result["total"]["opens"] == expected_opens,
            f"the count scenario uses {expected_opens} database open requests",
        )


def verify_invalidation_contract(result):
    require(result["secondUpdate"] == 2, "the external writer advances only the affected key")
    require(
        result["unrelatedAfterSignal"] == result["firstUnrelated"],
        "an external signal leaves an unrelated cached document unchanged",
    )
    require(
        result["affectedAfterSignal"] == {"revision": 2, "json": '{"owner":"second"}'},
        "an external signal makes the affected key reload its durable replacement",
    )
    require(result["unrelatedReadTransactions"] == 0, "the unrelated key remains hot")
    require(result["affectedReadTransactions"] == 1, "the affected key performs one bounded reload")


def verify_conflict_without_signal_contract(result):
    require(result["delayedMessages"] > 0, "the delayed-channel fixture withheld invalidation messages")
    for signal, outcome in (("delayed", result["delayed"]), ("absent", result["absent"])):
        require(
            outcome["staleHotRead"] == outcome["oldValue"],
            f"an old tab can retain a value while signaling is {signal}",
        )
        require(outcome["conflict"] is None, f"CAS rejects a stale write when signaling is {signal}")
        require(
            outcome["reloaded"] == {"revision": 2, "json": '{"owner":"current"}'},
            f"a {signal} signal cannot prevent conflict reload",
        )
        require(outcome["hotReload"] == outcome["reloaded"], f"the {signal}-signal reload becomes hot")
        require(
            outcome["conflictAndReloadTransactions"] == 2,
            f"{signal} signaling remains safe through one update and one bounded reload",
        )


def verify_read_write_race_contract(result):
    require(
        result["staleRead"] == {"revision": 1, "json": '{"stage":"before"}'},
        "the deliberately delayed read observed the pre-write document",
    )
    require(result["updated"] == 2, "the racing write advances the durable revision")
    require(
        result["finalRead"] == {"revision": 2, "json": '{"stage":"after"}'},
        "the stale read cannot replace the newer cached document",
    )
    require(result["finalReadTransactions"] == 0, "the newer write-through document remains hot")
    require(result["total"]["transactions"] == 2, "the race uses one read and one update transaction")


def verify_failure_contract(result):
    committed = {"revision": 1, "json": '{"stage":"committed"}'}
    require(result["committedUpdate"] == committed, "the failure fixture begins with a committed document")
    require(
        result["transactionFailure"].startswith("InvalidStateError:"),
        "transaction creation failure retains its browser classification",
    )
    require(result["afterTransactionFailure"] == committed, "a failed update publishes no replacement")
    require(result["transactionFailureReloads"] == 1, "a failed update invalidates before one reload")
    require(result["updateAbort"] is not None, "an aborted update surfaces a storage failure")
    require(result["afterUpdateAbort"] == committed, "an aborted update publishes no replacement")
    require(result["updateAbortReloads"] == 1, "an aborted update invalidates before one reload")
    require(result["createAbort"] is not None, "an aborted create surfaces a storage failure")
    require(result["afterCreateAbort"] is None, "an aborted create publishes no document")
    require(result["createAbortReloads"] == 1, "an aborted create invalidates cached absence")
    require(result["deleteAbort"] is not None, "an aborted delete surfaces a storage failure")
    require(
        result["afterDeleteAbort"] == committed,
        "an aborted delete does not publish cached absence",
    )
    require(result["deleteAbortReloads"] == 1, "an aborted delete invalidates before one reload")


def verify_lifecycle_contract(result):
    require(
        result["afterConcurrentFirstUse"] == {"openAttempts": 1, "opens": 1, "transactions": 3},
        "concurrent first use shares one successful database open",
    )
    require(
        result["afterVersionChange"] == {"openAttempts": 2, "opens": 2, "transactions": 4},
        "version change closes the old handle and the next operation reopens once",
    )
    require(result["afterClose"] is None, "close-event recovery completes a cold read")
    require(
        result["afterClosure"] == {"openAttempts": 3, "opens": 3, "transactions": 5},
        "database closure invalidates the cached handle and reopens once",
    )
    require(
        result["afterPageHide"] == {"revision": 1, "json": '{"stage":"versionchange"}'},
        "page-hide disposal clears the document cache before a durable reload",
    )
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
    require(result["afterOpenRecovery"] is None, "a later open recovers after an open failure")
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
        result["afterOperationRecovery"] is None,
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
                    chrome.devtools.wait_for("document.readyState === 'complete'", "fresh invalidation evidence page")
                    invalidation = invalidation_scenario(chrome.devtools)
                    print(f"INVALIDATION {json.dumps(invalidation, sort_keys=True)}")
                    verify_invalidation_contract(invalidation)

                    chrome.devtools.command("Page.reload", {"ignoreCache": True})
                    chrome.devtools.wait_for("document.readyState === 'complete'", "fresh unsignalled evidence page")
                    unsignalled = conflict_without_signal_scenario(chrome.devtools)
                    print(f"UNSIGNALLED {json.dumps(unsignalled, sort_keys=True)}")
                    verify_conflict_without_signal_contract(unsignalled)

                    chrome.devtools.command("Page.reload", {"ignoreCache": True})
                    chrome.devtools.wait_for("document.readyState === 'complete'", "fresh race evidence page")
                    race = read_write_race_scenario(chrome.devtools)
                    print(f"RACE {json.dumps(race, sort_keys=True)}")
                    verify_read_write_race_contract(race)

                    chrome.devtools.command("Page.reload", {"ignoreCache": True})
                    chrome.devtools.wait_for("document.readyState === 'complete'", "fresh failure evidence page")
                    failures = failure_scenario(chrome.devtools)
                    print(f"FAILURES {json.dumps(failures, sort_keys=True)}")
                    verify_failure_contract(failures)

                    chrome.devtools.command("Page.reload", {"ignoreCache": True})
                    chrome.devtools.wait_for("document.readyState === 'complete'", "fresh lifecycle evidence page")
                    lifecycle = lifecycle_scenario(chrome.devtools)
                    print(f"LIFECYCLE {json.dumps(lifecycle, sort_keys=True)}")
                    verify_lifecycle_contract(lifecycle)
            finally:
                chrome.close()


if __name__ == "__main__":
    main()
