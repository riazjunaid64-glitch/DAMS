# Meta integration setup

How to set up the Meta app so that **CRM → Settings → Connect Meta** works in an
environment. Do this once for each environment (staging, production).

## 1. Settings DAMS needs

These are secrets. Put them in user-secrets or environment variables
(`MetaIntegration__AppId` and so on), never in a committed appsettings file.

| Setting | Required | Value |
| --- | --- | --- |
| `MetaIntegration:AppId` | yes | App ID from the App Dashboard |
| `MetaIntegration:AppSecret` | yes | App Secret |
| `MetaIntegration:WebhookVerifyToken` | yes | Any random string. Enter the same string in the webhook subscription |
| `MetaIntegration:OAuthCallbackUrl` | yes | `https://<api-host>/api/integrations/meta/callback`. Must be HTTPS and must also be listed under **Valid OAuth Redirect URIs** |
| `MetaIntegration:FrontendReturnUrl` | when the frontend is on a different host | `https://<frontend-host>/crm/settings` |
| `MetaIntegration:LoginConfigId` | Business apps only | The configuration ID from step 2 (digits only) |

Set the first four together, or leave all four unset. With only some of them set, the API
refuses to start. It also refuses to start if `LoginConfigId` is set to anything other
than digits.

### Graph API version

DAMS uses Graph API **v25.0**. Meta supports it until **29 July 2028**. The version is set in
`MetaIntegration:GraphApiVersion` (`appsettings.json`, default in `MetaIntegrationOptions`),
and it is used both for Graph calls and for the login dialog URL.

In the App Dashboard, under **Webhooks → Page**, set the webhook version to the same
**v25.0**. The webhook version is set separately from the calls DAMS makes.

Upgrade well before the expiry date. After it, Meta does not reject calls to the old
version. It runs them on the oldest version still available, so behaviour can change without
any error. To upgrade, read the Graph and Marketing API changelogs for changes to Lead,
LeadgenForm, Page `subscribed_apps`, `me/accounts` and webhooks. Then change the setting,
the default, the tests and the webhook version together, and update the version and date here.

## 2. Which login product to use

Look at the app type in the App Dashboard.

### Business app: Facebook Login for Business

The permissions DAMS needs are business permissions, so the client's app will normally be
a Business app. Business apps log in through **Facebook Login for Business**, and the login
dialog needs a login configuration.

1. Add the **Facebook Login for Business** product to the app.
2. Under **Settings**, add `OAuthCallbackUrl` to **Valid OAuth Redirect URIs**.
3. Under **Configurations**, create a configuration:
   - **Login variation:** General.
   - **Access token type: User access token.** Do not choose *System-user access token*
     (see the note below).
   - **Assets:** Pages. Add Ad accounts too if campaign discovery is wanted.
   - **Permissions:** add every permission below.

     | Permission | Why | Required |
     | --- | --- | --- |
     | `pages_show_list` | List the Pages this person manages | yes |
     | `pages_read_engagement` | Read Page details and the linked Instagram account | yes |
     | `pages_manage_metadata` | Subscribe the Page to the leadgen webhook | yes |
     | `leads_retrieval` | Read each submitted lead | yes |
     | `ads_read` | Read-only campaign, ad set and ad discovery | recommended |
     | `instagram_basic` | Label leads that came from Instagram correctly | recommended |

     This list must match `MetaScopes` in
     `BACKEND/DAMS.Application/Common/IntegrationConstants.cs`. If one of the four
     required permissions is missing, the connection is saved as **Needs reconnection**
     and no leads are fetched.
4. Copy the **Configuration ID** into `MetaIntegration:LoginConfigId`, then restart the API.

With `LoginConfigId` set, Connect opens the dialog with `config_id` and does not send
`scope`. Meta recommends not sending `scope` with a configuration. If an FLfB app gets
only `scope`, the dialog stops at "Feature unavailable — Facebook Login is currently
unavailable for this app", and nothing reaches DAMS, so DAMS logs nothing either.

`auth_type=rerequest` is not sent with a configuration. Meta documents it only for the
scope-based dialog. On the staging test, decline one permission, click Reconnect, and
check that the dialog asks for it again. If it does not, the person has to remove the app
under Facebook **Settings → Business integrations** and then connect again.

**Why not a System-user access token.** Meta says a system-user token "defaults to never
expire", which would remove the 60-day expiry. DAMS cannot use one yet. It would need
`override_default_response_type=true` on the dialog, and the callback turns the code into
a long-lived *user* token (`fb_exchange_token`). Nobody has checked that this exchange, or
`me/accounts` and the Page tokens it returns, work for a system user. Choosing it before
that is checked and built will make Connect fail.

### Consumer app: classic Facebook Login

Leave `LoginConfigId` unset. Connect then uses the classic dialog, with `scope` set to the
permissions above and `auth_type=rerequest`, so that Reconnect asks again for permissions
that were declined. Add `OAuthCallbackUrl` to **Valid OAuth Redirect URIs** under
Facebook Login → Settings.

## 3. Check it on staging

1. Click **Connect Meta** as a person who can advertise on the Page and has lead access in
   Leads Access Manager.
2. The connection shows **Connected**, and every required permission is granted. If it
   shows **Needs reconnection**, its message lists the permissions that are missing. Add
   them to the login configuration.
3. Send a lead with Meta's Lead Ads Testing Tool and check that it appears in the CRM.
4. Check that the webhook version in the App Dashboard matches `GraphApiVersion`, so this
   test runs on the same version that production will use.

## 4. Token expiry and alerts

Connect stores a long-lived **user** token. Meta says it "generally lasts about 60 days". Leads
are fetched with the **Page** tokens that `me/accounts` returns, and those do not expire with it.

- When Meta refuses the user token during the 6-hourly sync, the connection stays
  **Connected**. The panel shows a warning, Pages and lead forms stop being refreshed, and
  leads keep arriving through the Page tokens. The sync tries again after the normal interval.
- Only when a Page token itself is refused (or no Page token is stored) does the connection
  go to **Needs reconnection**. Its leads are then parked, not lost.
- Reconnecting stores a new user token only. The worker's first sync after it (within a
  minute) fetches new Page tokens, and at the end of that sync the parked leads are released
  and fetched on the next tick. Leads that arrive between the reconnect and that sync wait for
  it too, rather than being fetched with a Page token the old sign-in handed out. If that sync
  has not got through within 30 minutes, leads are tried with the old token anyway.
- The panel's sign-in warning uses the same `TokenExpiryWarningDays` window as the alert below.

Every active Admin and every active Sales Manager gets one notification (category
**Integrations**, email and push if
those are switched on) when:

| Alert | When | Setting |
| --- | --- | --- |
| Needs reconnecting | A connection is in Needs reconnection. Once per connect. | — |
| Sign-in expires soon | The user token expires within the warning window. Once per token. | `MetaIntegration:TokenExpiryWarningDays` (default 7; 0 = off) |
| Lead events failed | Events on a connection reached Failed. Once per connection per day. | — |
| Page gone quiet | An enabled Page that received leads before has had none for N days. | `MetaIntegration:QuietPageAlertDays` (default 0 = off) |

The quiet-Page alert is off by default because a Page with no campaign running is quiet for
good reason. Set it to the number of days the business considers too long.

A system-user token (see section 2) would remove the 60-day expiry and is still the preferred
long-term fix. It needs the live checks listed there before it can be supported.

## 5. Recovering leads the webhook missed

Meta retries a failed webhook for about 36 hours and then gives up. It keeps every lead
readable through its form (`GET /{form-id}/leads`) for 90 days, and DAMS uses that to recover
leads that never arrived:

- **Reconciliation.** Every resource sync (every 6 hours) reads the last
  `MetaIntegration:ReconciliationLookbackHours` (default 48; 0 = off) of every lead form on
  every enabled Page, with the Page's own token. It stops 15 minutes short of now: those leads
  are the webhook's, and reading them too would drop their webhook as a duplicate.
  Separately, reconciliation puts a stored event that failed for a transient reason (Meta could
  not be reached, a timeout, a rate limit, or a database error) back on the queue from DAMS's
  own rows — not only when that lead is inside the lookback window. Up to three extra rounds,
  while the lead is still within the 90 days Meta keeps it, and only for a Page that is still
  enabled. A permanent failure — a payload with no lead id, an id DAMS cannot store, or a lead
  Meta says does not exist — stays failed. Those, and any that have used their automatic
  rounds, are retried from the event list or with **Retry now**. Retry now says when it left
  an event alone because its Page is switched off or Meta no longer returns it.
- **Import.** CRM settings → Integrations → Manage resources → **Import leads** on an enabled
  Page reads its synced forms back to a chosen date, at most 90 days ago, and reports how many
  leads were found, new, already in DAMS, previously failed and failed. Run **Sync now** first if
  the Page's forms are not listed yet. An import counts a failed event and leaves it failed.

A window with more leads than one read allows (`MaxGraphPages` pages of 100) is split in halves
and each half read on its own, so older leads are not skipped. Only an hour with more leads than
one read allows is reported as incomplete.

Each recovered lead is queued as a `leadgen_backfill` event under the same key the webhook
would have used, so a lead the webhook already delivered is counted, never added twice. A lead
the webhook recorded while its Page was off is picked up again once the Page is enabled.
After that, the normal processor handles it exactly like a webhook lead: duplicates, held
enquiries and attribution. The lead keeps Meta's `created_time` as its submission time.

**Alerts for recovered leads.** A recovered lead Meta says was submitted more than
`MetaIntegration:BackfillAlertCutoffHours` ago (default 48; 0 = every recovered lead) is added
without a "new lead" or repeat-enquiry notification, and its timeline says it was imported. The
import reports how many were added this way, so a manager can assign them from the Leads queue.
Younger recovered leads still need a call now, and notify as usual. Webhook leads always notify.
This follows the recommendation on KAN-35, which is still waiting for the product owner's
confirmation. First-response alerts are timed from when DAMS assigns the lead, so a recovered
lead is not reported overdue on arrival.

**Admin and Sales Manager alerts.** Each reconciliation's outcome is kept on the connection,
and every Admin and Sales Manager is told through the notification system when:

- reconciliation fails on two runs in a row, for example a Page credential that can no longer be
  read, or Meta refusing the lead read (see `pages_manage_ads` below). The alert includes
  Meta's reason.
- reconciliation finds leads the webhook never delivered (once per day). Unless a Page was just
  enabled, this means the webhook is dropping leads.

## 6. Before go-live

Do this once on the production Meta app, after the settings in section 1 are in place.
The code subscribes each Page it is given (`subscribed_apps`), but the app-level webhook
subscription is not something DAMS can turn on.

1. **Webhook.** In the App Dashboard, under **Webhooks → Page**, set the callback URL to
   `https://<api-domain>/api/integrations/meta/webhook` and the verify token to the same
   value as `MetaIntegration:WebhookVerifyToken`. Subscribe the Page object to the
   **leadgen** field. The webhook version should be the same **v25.0** as section 1.
2. **App mode and review.** Switch the app to **Live**. Request Advanced Access (App Review)
   for `leads_retrieval`, `pages_manage_metadata`, `pages_read_engagement` and
   `pages_show_list`. Those are the lead-critical scopes in
   `BACKEND/DAMS.Application/Common/IntegrationConstants.cs` (`MetaScopes.LeadCritical`).
   `ads_read` and `instagram_basic` are optional; what they do is in section 2.
3. **Leads Access.** In Business Suite go to **Settings → Leads access** (Leads Access
   Manager). Give access to the person who will click Connect, and to the DAMS app (the CRM).
   Without this, Meta accepts the connection and then refuses the lead.
4. **Test tool.** Use Meta's Lead Ads Testing Tool on the Page. Delete the previous test
   lead before creating the next one. Test leads share dummy contact details, so the second
   one can be added to the first test lead instead of creating a new one.
5. **When a lead does not arrive.** On the Integrations page, check the connection status
   and the failed-lead count (events that failed in the last seven days, and when the last
   one was tried). Open the event list for the reason. **Retry now** puts that connection's
   failed events back on the queue, and says when it left one alone because its Page is
   switched off or Meta no longer returns it. The API logs record a Meta or database error
   that did not get as far as an event.

Not built yet:

- Importing Meta's CSV exports for leads older than 90 days (needs a product decision).
- Meta's docs list `pages_manage_ads` for bulk lead reads. DAMS does not ask for it. If the
  staging test shows it is needed, a manual import shows Meta's permission error and scheduled
  reconciliation raises the alert above. Add the scope only with that evidence (see
  `MetaScopes`).
