# Unreleased

## Shared date and time pickers (KAN-70)

Every built-in date, time and date-time input in the app is replaced by one shared date picker and one shared time picker. Dates show as "Sep 29, 2026" and times as "11:30 AM" on desktop and phone; forms still send `YYYY-MM-DD` and `HH:mm`, so no server change is needed. "Today" is today in Pakistan. Payment, refund, date-of-birth and other finance dates cannot be picked in the future, and the cancellation refund date is locked to today, which is the only date the server accepts for it. The notification admin's send time now uses the date and time pair. The salary month selector is unchanged.

## Proof on customer payments and refunds (KAN-71)

A shared **Attach proof** upload field (one file, PDF, image, Word or Excel, up to 15 MB, checked in the browser first) is added to the component library. The server now accepts one proof file for each customer payment and each cancellation refund, using the same private storage, file checks, Admin and Accountant access and audit rows as commission and rebate proof (`POST /api/finance/commissions-rebates/evidence/CustomerPayment/{id}` and `.../CancellationRefund/{id}`). The booking detail returns `proof` (id, file name, size) on each payment and on the refund; a customer's own view of a booking never includes it.

Deploying this runs the `AddPaymentAndRefundProof` migration: two nullable columns on `FinancialEvidence`, unique filtered indexes (one file per payment or refund), and a wider "exactly one owner" check constraint. Nothing is backfilled.

## Site visit reminders (KAN-30)

Site visit reminders now follow the admin notification rule, using Pakistan calendar days. Visits without a custom reminder are announced on the configured number of days before the visit (one day by default); setting lead days to zero disables that advance reminder. A visit's own reminder time replaces the admin advance timing. The separate visit-day reminder runs only when **Remind on due date** is enabled. Turning off the Site visit reminder rule stops both reminders, while missed-visit marking and escalation continue.

Existing scheduled visits within the advance window may receive a reminder on the first scan after this release. Rescheduled visits can receive reminders again for their new schedule.
