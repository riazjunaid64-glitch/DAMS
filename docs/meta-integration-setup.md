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
