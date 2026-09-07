import { describe, expect, it } from "vitest";
import { createPanelRefreshSignal, mutationSnapshotIsCurrent } from "./refreshCoordination";

/**
 * Booking Detail and its Commission & Rebate panel show the same money from two copies. These tests
 * drive the exact overlapping-response orderings that let the two disagree — the page reporting
 * Rs 12,000 collected beside a panel still reporting Rs 10,000, with no warning that either is old.
 *
 * Both orderings turn on a response arriving LATE, so they are written as explicit sequences rather
 * than as a rendered click-through, which would only ever exercise the fast path.
 */
describe("keeping the panel in step across overlapping reloads", () => {
  it("delivers a notification recorded by a reload that was later superseded", () => {
    const signal = createPanelRefreshSignal();

    // 1. A payment recorded on another tab starts a reload that must notify the panel.
    signal.reloadStarted(true);
    // 2. A slow rebate response arrives and starts its own reload, which deliberately does not
    //    notify — its own answer already carried the new workspace.
    signal.reloadStarted(false);
    // 3. The payment's reload finds it has been superseded and returns without delivering.
    //    Its ANSWERS are stale; its message is not, so it must still be owed.
    expect(signal.outstanding).toBe(true);

    // 4. The rebate's reload finishes as the current one and hands on what it inherited.
    expect(signal.deliver()).toBe(true);
    expect(signal.outstanding).toBe(false);
  });

  it("does not notify the panel for a rebate mutation on its own", () => {
    const signal = createPanelRefreshSignal();

    // This is the whole point of the three-request flow: the mutation answered with the workspace,
    // so the page's reload must not send the panel back to re-read it.
    signal.reloadStarted(false);

    expect(signal.deliver()).toBe(false);
  });

  it("notifies once for any number of overlapping external changes", () => {
    const signal = createPanelRefreshSignal();

    signal.reloadStarted(true);
    signal.reloadStarted(true);
    signal.reloadStarted(false);

    expect(signal.deliver()).toBe(true);
    // Delivered means delivered: the next reload to finish must not bump the panel again.
    expect(signal.deliver()).toBe(false);
  });

  it("keeps owing the notification until a reload actually finishes", () => {
    const signal = createPanelRefreshSignal();

    signal.reloadStarted(true);
    expect(signal.outstanding).toBe(true);
    // Several superseded reloads in a row still leave it owed.
    signal.reloadStarted(false);
    signal.reloadStarted(false);
    expect(signal.outstanding).toBe(true);
    expect(signal.deliver()).toBe(true);
  });

  it("starts owing nothing", () => {
    expect(createPanelRefreshSignal().outstanding).toBe(false);
    expect(createPanelRefreshSignal().deliver()).toBe(false);
  });
});

describe("whether a mutation's own answer is still the newest", () => {
  it("accepts the answer when nothing else moved the booking", () => {
    // The ordinary case, and the one worth saving a request on.
    expect(mutationSnapshotIsCurrent(4, 4)).toBe(true);
  });

  it("rejects an answer that a payment from another tab has already overtaken", () => {
    // The panel was refreshed to Rs 12,000 while the rebate response was in flight. Applying that
    // response would put Rs 10,000 back on screen.
    expect(mutationSnapshotIsCurrent(4, 5)).toBe(false);
  });

  it("rejects it however many external changes landed meanwhile", () => {
    expect(mutationSnapshotIsCurrent(0, 1)).toBe(false);
    expect(mutationSnapshotIsCurrent(2, 9)).toBe(false);
  });
});
