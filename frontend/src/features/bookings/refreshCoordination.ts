/**
 * Booking Detail and its Commission & Rebate panel each hold their own copy of the same booking
 * figures, refreshed from different places. Keeping the two in step is a matter of not losing —
 * and not ignoring — the message that says the booking has moved.
 *
 * Two overlapping refreshes are enough to drop that message, which is why these rules are here
 * rather than inline: they are ordering rules, and ordering is what a rendered click-through is
 * least likely to catch.
 */

/**
 * Carries "the panel still has to be told the booking moved" across reloads that supersede one
 * another.
 *
 * The page discards a reload whose answers have been overtaken by a newer one. That is right for
 * the DATA — the newer answers are the current ones — but the message was being discarded with it.
 * A payment recorded on another tab starts a reload that WILL notify the panel; a rebate mutation's
 * delayed response then starts its own reload that deliberately will NOT (its own response already
 * carried the new workspace). The second supersedes the first, the first returns before notifying,
 * and the page ends up showing Rs 12,000 collected beside a panel still showing Rs 10,000, with
 * nothing on screen admitting the difference.
 *
 * So the message is held here, outside any one reload, until it is actually delivered. Recording it
 * when a reload STARTS is the point: by the time a reload finds out it has been superseded, it is
 * already too late for it to hand anything on.
 */
export interface PanelRefreshSignal {
  /** A reload is starting. `notifiesPanel` is false only for the panel's own mutations. */
  reloadStarted(notifiesPanel: boolean): void;
  /** A reload has finished as the current one. True when the panel must re-read. */
  deliver(): boolean;
  /** Whether a notification is still owed. */
  readonly outstanding: boolean;
}

export function createPanelRefreshSignal(): PanelRefreshSignal {
  let owed = false;
  return {
    reloadStarted(notifiesPanel: boolean) {
      if (notifiesPanel) owed = true;
    },
    deliver() {
      const due = owed;
      owed = false;
      return due;
    },
    get outstanding() {
      return owed;
    },
  };
}

/**
 * Whether a mutation's response still describes the newest booking.
 *
 * A mutation answers with the whole workspace as it stood when the server handled it, and the panel
 * pins its screen to that answer instead of re-reading. That is only sound while nothing else moved
 * the booking in the meantime. If a payment landed from another tab while the mutation was in
 * flight, the panel has already been refreshed with a newer workspace, and applying the mutation's
 * older snapshot on top of it silently puts the previous "Amount collected" back.
 *
 * Counted in external changes — the page's refresh token — rather than in time, because that is the
 * only signal the panel gets that something other than itself moved the booking.
 */
export const mutationSnapshotIsCurrent = (externalChangesAtStart: number, externalChangesNow: number) =>
  externalChangesAtStart === externalChangesNow;
