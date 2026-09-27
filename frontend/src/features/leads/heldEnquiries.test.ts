import { describe, expect, it } from "vitest";
import { describeHeldEnquiry, heldEnquiriesTitle, type HeldEnquiry } from "./heldEnquiries.ts";

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
    { leadId: 1, leadReference: "LD-000001", leadName: "Person A", leadStage: "New", leadStageGroup: "New", leadOwnerName: "Sana", matchedOn: "phone", isOpen: true },
    { leadId: 2, leadReference: "LD-000002", leadName: "Person B", leadStage: "Lost", leadStageGroup: "Lost", leadOwnerName: null, matchedOn: "email", isOpen: false },
  ],
  ...overrides,
});

describe("describeHeldEnquiry", () => {
  it("shows who the enquiry is from and where it came from", () => {
    const view = describeHeldEnquiry(enquiry());

    expect(view.title).toBe("Who Is This");
    expect(view.contact).toEqual(["0300-1234567", "b@example.com"]);
    expect(view.origin).toBe("Facebook");
  });

  it("lists a WhatsApp number only when it differs from the phone", () => {
    expect(describeHeldEnquiry(enquiry({ whatsappNumber: "0300-1234567" })).contact).toEqual(["0300-1234567", "b@example.com"]);
    expect(describeHeldEnquiry(enquiry({ whatsappNumber: "0311-7654321" })).contact).toEqual(["0300-1234567", "0311-7654321", "b@example.com"]);
  });

  it("offers each matched lead with its status, owner and what it matched; a closed lead cannot take it", () => {
    const [a, b] = describeHeldEnquiry(enquiry()).matches;

    expect(a).toEqual({
      leadId: 1,
      leadName: "Person A",
      leadReference: "LD-000001",
      status: "InProgress",
      ownerName: "Sana",
      matched: "Same phone",
      canAdd: true,
    });
    expect(b).toMatchObject({ status: "Lost", ownerName: "Unassigned", matched: "Same email", canAdd: false });
  });

  it("never offers to dismiss an enquiry a website booking request is waiting on", () => {
    expect(describeHeldEnquiry(enquiry()).canDismiss).toBe(true);
    expect(describeHeldEnquiry(enquiry({ bookingRequestId: 42 })).canDismiss).toBe(false);
  });

  it("falls back sensibly when details are missing", () => {
    const view = describeHeldEnquiry(enquiry({ firstName: "", lastName: null, sourceName: null, provider: null, phone: null, email: null }));

    expect(view.title).toBe("Unnamed enquiry");
    expect(view.origin).toBe("External enquiry");
    expect(view.contact).toEqual([]);
  });
});

describe("heldEnquiriesTitle", () => {
  it("counts what is waiting", () => {
    expect(heldEnquiriesTitle(1)).toBe("1 enquiry needs your decision");
    expect(heldEnquiriesTitle(2)).toBe("2 enquiries need your decision");
    expect(heldEnquiriesTitle(1250)).toBe("1,250 enquiries need your decision");
  });
});
