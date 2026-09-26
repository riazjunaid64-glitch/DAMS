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

## 4. Token expiry and alerts

Connect stores a long-lived **user** token. Meta says it "generally lasts about 60 days". Leads
are fetched with the **Page** tokens that `me/accounts` returns, and those do not expire with it.

- When Meta refuses the user token during the 6-hourly sync, the connection stays
  **Connected**. The panel shows a warning, Pages and lead forms stop being refreshed, and
  leads keep arriving through the Page tokens. The sync tries again after the normal interval.
- Only when a Page token itself is refused (or no Page token is stored) does the connection
  go to **Needs reconnection**. Its leads are then parked, not lost.
- Reconnecting releases the parked leads, so they are fetched on the worker's next tick.

Every active Admin gets one notification (category **Integrations**, email and push if
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
  every enabled Page, with the Page's own token.
- **Import.** CRM settings → Integrations → Manage resources → **Import leads** on an enabled
  Page reads its synced forms back to a chosen date, at most 90 days ago, and reports how many
  leads were found, new, already in DAMS, and failed. Run **Sync now** first if the Page's forms
  are not listed yet.

Each recovered lead is queued as a `leadgen_backfill` event under the same key the webhook
would have used, so a lead the webhook already delivered is counted, never added twice. A lead
the webhook recorded while its Page was off is picked up again once the Page is enabled.
After that, the normal processor handles it exactly like a webhook lead: duplicates, held
enquiries, attribution and the "new lead" notification. The lead keeps Meta's `created_time`
as its submission time. First-response alerts are timed from when DAMS assigns the lead, so a
recovered lead is not reported overdue on arrival.

Not built yet:

- Importing Meta's CSV exports for leads older than 90 days (needs a product decision).
- Meta's docs list `pages_manage_ads` for bulk lead reads. DAMS does not ask for it. If the
  staging test shows it is needed, the import and reconciliation report Meta's permission
  error; add the scope only with that evidence (see `MetaScopes`).
