import { describe, expect, it } from "vitest";
import { describeHeldEnquiry, heldEnquiriesHeading, type HeldEnquiry } from "./heldEnquiries.ts";

const enquiry = (overrides: Partial<HeldEnquiry> = {}): HeldEnquiry => ({
  id: 7,
  receivedAt: "2026-09-24T10:00:00Z",
  provider: "meta",
  sourceName: "Facebook",
  firstName: "Who",
  lastName: "Is This",
  phone: "0300-1234567",
  email: "b@example.com",
  campaignName: "Summer Launch",
  candidates: [
    { leadId: 1, leadReference: "LD-000001", leadName: "Person A", leadStage: "New", leadOwnerName: "Sana", matchedOn: "phone", isOpen: true },
    { leadId: 2, leadReference: "LD-000002", leadName: "Person B", leadStage: "Lost", leadOwnerName: null, matchedOn: "email", isOpen: false },
  ],
  ...overrides,
});

describe("describeHeldEnquiry", () => {
  it("shows who the enquiry is from and where it came from", () => {
    const view = describeHeldEnquiry(enquiry());

    expect(view.title).toBe("Who Is This");
    expect(view.contact).toEqual(["Phone 0300-1234567", "Email b@example.com"]);
    expect(view.origin).toBe("Facebook · Summer Launch");
  });

  it("offers each matched lead with what it matched, adding only to that lead", () => {
    const [a, b] = describeHeldEnquiry(enquiry()).choices;

    expect(a).toEqual({
      leadId: 1,
      title: "LD-000001 · Person A",
      detail: "Matches this enquiry's phone number · owned by Sana",
      addLabel: "Add to LD-000001",
      blockedReason: null,
    });
    expect(b.detail).toBe("Matches this enquiry's email address · unassigned");
    expect(b.blockedReason).toMatch(/closed. Reopen it first/);
  });

  it("never offers to dismiss an enquiry a website booking request is waiting on", () => {
    expect(describeHeldEnquiry(enquiry()).canDismiss).toBe(true);
    expect(describeHeldEnquiry(enquiry()).waitingNote).toBeNull();

    const waiting = describeHeldEnquiry(enquiry({ bookingRequestId: 42 }));
    expect(waiting.canDismiss).toBe(false);
    expect(waiting.waitingNote).toBe("Website booking request #42 cannot be approved until you choose its lead.");
  });

  it("falls back sensibly when details are missing", () => {
    const view = describeHeldEnquiry(enquiry({ firstName: "", lastName: null, sourceName: null, provider: null, campaignName: null, phone: null, email: null }));

    expect(view.title).toBe("Unnamed enquiry");
    expect(view.origin).toBe("External enquiry");
    expect(view.contact).toEqual([]);
  });
});

describe("heldEnquiriesHeading", () => {
  it("counts what is waiting, and says when more are waiting than are listed", () => {
    expect(heldEnquiriesHeading({ totalWaiting: 2, items: [enquiry(), enquiry({ id: 8 })] })).toBe("Held enquiries (2)");
    expect(heldEnquiriesHeading({ totalWaiting: 250, items: [enquiry()] })).toBe("Held enquiries (250, showing the oldest 1)");
  });
});
