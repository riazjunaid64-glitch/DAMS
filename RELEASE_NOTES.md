# Unreleased

## Site visit reminders (KAN-30)

Site visit reminders now follow the admin notification rule, using Pakistan calendar days. Visits without a custom reminder are announced on the configured number of days before the visit (one day by default); setting lead days to zero disables that advance reminder. A visit's own reminder time replaces the admin advance timing. The separate visit-day reminder runs only when **Remind on due date** is enabled. Turning off the Site visit reminder rule stops both reminders, while missed-visit marking and escalation continue.

Existing scheduled visits within the advance window may receive a reminder on the first scan after this release. Rescheduled visits can receive reminders again for their new schedule.
