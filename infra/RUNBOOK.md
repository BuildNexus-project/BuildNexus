# Runbook — Azure deployment

Operational steps for the deployed stack: provisioning it, redeploying a single
service, rolling one back, and the manual steps that are not automated and
cannot be.

For the local development stack — docker compose, Mailpit, the per-service MySQL
containers — see `README.md` in this directory instead. Nothing here affects a
local run.

## What is deployed

| Thing | Name | Notes |
|---|---|---|
| Resource group | `buildnexus-rg` | The whole application stack. Destroying it destroys everything below. |
| App Service plan | `buildnexus-asp` | Linux, B1. Shared by every service. |
| MySQL Flexible Server | `buildnexus-mysql-2026` | Shared. One *database* per service on it. |
| User Service | `buildnexus-user-service-2026` | <https://buildnexus-user-service-2026.azurewebsites.net> |
| User Service database | `buildnexus_user_db` | On the shared server above. |

Terraform's own state lives somewhere else entirely — resource group
`buildnexus-tfstate-rg`, storage account `buildnexustfstate2026`, container
`tfstate`, key `user-service/terraform.tfstate`. That resource group is **not**
managed by Terraform and must never be destroyed by it: a module cannot hold the
storage its own state lives in.

Everything is in **southeastasia**. This is not a preference, and two separate
restrictions produced it — they fail differently, so both are worth knowing
before anyone tries to move the stack.

**1. An Azure Policy limits the subscription to five regions.** The assignment
is called *Allowed resource deployment regions* and permits exactly:

```
southeastasia, eastasia, centralindia, uaenorth, austriaeast
```

Anything else is refused at deployment time with `RequestDisallowedByAzure`,
which is how `eastus` was ruled out. Read the current list with:

```
az policy assignment list --query "[].{name:displayName, params:parameters}" -o json
```

**2. Of those five, centralindia cannot host MySQL on this subscription.**
Creating a Flexible Server there fails with `ProvisionNotSupportedForRegion`,
and the region's MySQL capability endpoint returns HTTP 500 for every API
version rather than an empty SKU list — so there is no smaller Burstable size to
fall back to, because the whole capability set is unavailable. The other four
regions each return the full set of nine Burstable SKUs including
`Standard_B1ms`. The resource group and App Service plan create in centralindia
without complaint; it is only MySQL that fails there, which is what makes this
worth writing down rather than rediscovering.

southeastasia is allowed by the policy, has working MySQL, and is the closest of
the four to the team. The stack stays in **one** region deliberately: putting
MySQL elsewhere while the App Service stayed in centralindia would cross a region
boundary on every query.

The location is written out as a literal on every resource in `terraform/main.tf`
and `terraform/user-service.tf` so it cannot be overridden back to a region that
fails.

## Why there is no Service Principal, and no infra.yml

**Terraform runs locally, from a developer's machine. It is never run by GitHub
Actions.** This is a hard constraint, not a shortcut, and it applies to every
deployment story — not just the User Service's.

The university's Azure AD tenant blocks Service Principal creation for student
accounts. A CI-triggered `terraform apply` or `destroy` needs one: a workflow
runner has no interactive session to authenticate with, so it needs
`ARM_CLIENT_ID` / `ARM_CLIENT_SECRET` / `ARM_TENANT_ID`, and those cannot be
created on this tenant.

So, concretely, none of the following exist anywhere in this repository, and
none of them should be added:

- `.github/workflows/infra.yml`, or any workflow that runs Terraform
- `ARM_CLIENT_ID`, `ARM_CLIENT_SECRET`, `ARM_TENANT_ID` — as Terraform
  variables, GitHub Secrets, or anything else
- an `azure/login` step in `ci.yml`

The `deploy-<service>` jobs in `ci.yml` are unaffected by this restriction and
work normally. Deploying already-built code to an App Service that already
exists authenticates with a **publish profile**, which is just credentials the
App Service itself issues. Only *provisioning new infrastructure* needs the
permission that is blocked.

## The Terraform stack

### One-time setup per machine

Terraform is not in the repository and is not installed by anything here.

```
winget install --id Hashicorp.Terraform -e --source winget
```

If winget reports success but installs nothing — it has been seen to do exactly
that — take the zip from <https://releases.hashicorp.com/terraform/>, extract
`terraform.exe` into `%LOCALAPPDATA%\Programs\terraform`, and add that folder to
your user PATH.

Then authenticate. Two separate mechanisms, which is the part that surprises
people:

1. **The azurerm provider**, which creates the actual resources, uses your own
   `az login` session. There is no credentials block in `provider.tf` and there
   must not be one.
2. **The state backend**, which reads and writes the state file in Azure
   Storage, authenticates with the storage account access key, read from the
   `ARM_ACCESS_KEY` environment variable. It does *not* use your `az login`
   session. Leave it unset and `terraform init` fails on the backend rather than
   the provider, which reads as a confusing error about the container.

PowerShell:

```
az login

$env:ARM_ACCESS_KEY = az storage account keys list --resource-group buildnexus-tfstate-rg --account-name buildnexustfstate2026 --query "[0].value" -o tsv
```

bash:

```
az login

export ARM_ACCESS_KEY=$(az storage account keys list \
  --resource-group buildnexus-tfstate-rg \
  --account-name buildnexustfstate2026 \
  --query '[0].value' -o tsv)
```

`ARM_ACCESS_KEY` is per-shell and is gone when the terminal closes. Set it again
in each new one. Never put it in a file.

Finally the variables with no default — the two signing keys and the MySQL
administrator password:

```
cd infra/terraform
cp terraform.tfvars.example terraform.tfvars    # then fill it in
```

`terraform.tfvars` is git-ignored. Generate fresh values with
`openssl rand -base64 48`; do **not** reuse the local-development values from
`infra/.env.example`, which are published in this repository on purpose. The
same `jwt_signing_key` must eventually be given to every other deployed service
and the API Gateway, or every token the User Service issues comes back 401.

### Apply

```
cd infra/terraform
terraform init      # first time in a working copy, or after changing provider.tf
terraform plan      # read this properly before applying
terraform apply
```

`terraform output` afterwards prints the public URL, the App Service name, the
resource group and the MySQL FQDN. No secret is an output.

A first apply takes several minutes, nearly all of it the MySQL Flexible Server.

**If apply fails specifically on `azurerm_mysql_flexible_server` with
`ProvisionNotSupportedForRegion`** — even though the resource group and App
Service plan succeeded — that is the failure described under "What is deployed"
above. It has happened once already, in centralindia, and the fix was to move the
whole stack to southeastasia.

Before assuming the region is at fault again, check whether it is the region or
the SKU, because they need different fixes and the error message conflates them.
The capability endpoint distinguishes them cleanly:

```
az mysql flexible-server list-skus --location <region> -o table
```

A region that returns the Burstable SKU list is fine and the problem is the
specific size; a region that errors or returns nothing cannot host MySQL at all
and no smaller size will help. Either way, bring the exact error to the team
rather than guessing — and do not move the MySQL server alone, because that
splits the stack across two regions.

### When apply fails partway

Azure's management API is eventually consistent. Southeastasia has been slow
enough about it to break a single first apply in four different ways — every one
of them a read-after-write 404, where the resource was created correctly and only
Terraform's record of it came back wrong.

That distinction is the whole point of this section: **none of these are
configuration errors, and none of them are fixed by recreating anything.** Read
what actually exists before acting.

```
az resource list --resource-group buildnexus-rg -o table
terraform state list
```

Compare the two. The mismatch tells you which case you have.

**1. It exists in Azure but is missing from state.** Apply fails with:

```
Error: a resource with the ID "..." already exists - to be managed via
Terraform this resource needs to be imported into the State
```

Re-running apply just repeats it — Terraform keeps trying to create what is
already there. Import it:

```
terraform import azurerm_service_plan.main \
  /subscriptions/<sub>/resourceGroups/buildnexus-rg/providers/Microsoft.Web/serverFarms/buildnexus-asp
```

The resource ID is **case-sensitive** in the segments the provider parses:
`serverFarms`, not `serverfarms`, or it fails with *"the parsed Resource ID was
missing a value for the segment at position 6"*, which does not sound like a
casing problem at all.

**2. It is in state but its attributes are null.** A create that failed during
the read-back writes a partial entry. The symptom is not obvious — every
subsequent command, `import` included, dies while evaluating outputs:

```
Error: Invalid template interpolation value
  azurerm_linux_web_app.user_service.default_hostname is null
```

Repopulate from Azure. This reads only; it changes nothing remote:

```
terraform apply -refresh-only -auto-approve
```

Do this **first** when several things are wrong at once, because the broken
output blocks the commands that would fix the rest.

**3. A healthy resource is marked tainted.** Terraform taints a resource whose
create partially failed, and a tainted resource is always replaced — so plan
proposes destroying something that is running perfectly well:

```
# azurerm_linux_web_app.user_service is tainted, so must be replaced
Plan: 1 to add, 0 to change, 1 to destroy.
```

`-refresh-only` does not clear this. Confirm the resource really is healthy, then
clear the flag rather than letting it be rebuilt — recreating an App Service also
regenerates its publish profile, which invalidates the GitHub Secret:

```
terraform untaint azurerm_linux_web_app.user_service
```

**4. The state is locked with no owner.** An interrupted command can leave the
blob lease held but the lock metadata empty:

```
Error: Error acquiring the state lock
Error message: state blob is already locked
blob metadata "terraformlockid" was empty
```

`terraform force-unlock` wants a lock ID, and there is not one to give it. Break
the lease in the Portal instead: storage account `buildnexustfstate2026` →
Containers → `tfstate` → `user-service/terraform.tfstate` → **Break lease**.
Check first that nobody else is actually running Terraform; the lock exists to
stop two people writing state at once, and this is only safe because it is stale.

Afterwards, `terraform plan` should report *No changes. Your infrastructure
matches the configuration.* Anything else means the reconciliation is not
finished — keep going rather than applying over it.

### Destroy

The stack is destroyed between demos to control Azure credit usage, and brought
back with `terraform apply`. That is the intended cycle, not an emergency
measure.

```
cd infra/terraform
terraform destroy
```

This deletes the resource group and everything in it. It does **not** touch
`buildnexus-tfstate-rg`, which is not managed here.

### What destroy takes with it — the post-apply checklist

`terraform apply` brings the *infrastructure* back identically. It does not
bring back anything that lived inside it. After every rebuild, all three of
these are gone and must be redone:

1. **The database contents.** The MySQL server is new and empty. DbUp recreates
   the schema at first startup, so the tables come back — the rows do not.
2. **The bootstrap Admin.** A consequence of (1). See below; there is no Admin
   account until someone makes one.
3. **The publish profile.** App Service regenerates its deployment credentials
   when it is recreated, so the `AZURE_WEBAPP_PUBLISH_PROFILE_USER_SERVICE`
   GitHub Secret is stale and the deploy job fails with a 401 that does not
   explain itself. Refresh it:

   ```
   az webapp deployment list-publishing-profiles \
     --resource-group buildnexus-rg \
     --name buildnexus-user-service-2026 \
     --xml > publish-profile.xml
   ```

   Paste the whole file into GitHub → Settings → Secrets and variables →
   Actions → `AZURE_WEBAPP_PUBLISH_PROFILE_USER_SERVICE`, then delete the local
   copy. It is a credential, and `*.xml` is not git-ignored.

## Redeploying User Service

### The normal path

Push a change under `services/user-service/**` to `main`. The
`deploy-user-service` job in `.github/workflows/ci.yml` runs after the unit test
stage, publishes that one project, pushes it to App Service, and then polls
`/health` until it answers 200. No other service is rebuilt or restarted.

A change anywhere else does not deploy the User Service — the job is filtered on
that path and skipped otherwise. A push to any branch other than `main` runs
build and tests and stops there.

### Forcing a redeploy without a code change

The deploy job only triggers on a path change, so there is nothing to push. Do it
directly — this uses your `az login` session, so no publish profile and no
Service Principal are involved:

```
dotnet publish services/user-service/UserService.csproj -c Release -o ./publish
cd publish && zip -r ../user-service.zip . && cd ..

az webapp deploy --resource-group buildnexus-rg \
  --name buildnexus-user-service-2026 \
  --src-path user-service.zip --type zip
```

To restart the process without deploying anything at all:

```
az webapp restart --resource-group buildnexus-rg --name buildnexus-user-service-2026
```

### Rolling back

**There are no deployment slots.** Slots need Standard tier or higher and the
plan is B1, so there is no staging slot to swap back. There is no container image
to re-tag either — this is a code deploy, not a container deploy. The options,
best first:

1. **Revert the commit and push.** `git revert <bad-sha>` on `main`. The deploy
   job runs on the revert and deploys the previous code. Slowest of the three,
   and the only one that leaves `main` and Azure agreeing with each other.

2. **Re-run the last green deploy job** from the Actions UI. Fast, and it deploys
   the code as it was at that commit — but `main` still contains the bad commit,
   so the next unrelated push under `services/user-service/**` redeploys it. A
   stopgap while you prepare the revert, not a fix.

3. **Deploy an older commit by hand**, using the `az webapp deploy` block above
   from a `git checkout` of a known-good SHA. Fastest, and it makes Azure diverge
   from `main` with nothing recording that it happened. Emergencies only, and
   follow it with a revert.

### When a deploy goes green but the service does not answer

The deploy step only proves the package was accepted; the `/health` step is what
proves the app started. When that step fails, read the log stream:

```
az webapp log tail --resource-group buildnexus-rg --name buildnexus-user-service-2026
```

The app validates its configuration at startup and refuses to boot on anything
missing — `Jwt__SigningKey` under 32 bytes, `InternalService__ApiKey` absent, a
connection string that does not reach MySQL. All of those surface here as a
startup exception rather than as a failing request.

## Creating the first Admin

Not automated, and deliberately a manual step. Required after every
`terraform destroy` / `apply` cycle, because the database is empty again.

Three things interlock to make the first Admin unreachable through the API:

- `AdminSeeder` is registered only under `IsDevelopment()` in `Program.cs`, and
  the deployed environment is `Production`. This is correct — the seeder uses a
  known development password, which must not exist on a public host.
- `POST /api/auth/register` refuses the `Admin` role. Self-service registration
  can only mint `Client`, `Architect` or `ProjectManager`.
- `PUT /api/users/{id}`, the only endpoint that changes a role, requires an
  authenticated Admin.

So: register a normal account through the API, then promote it with one UPDATE.
Do not hand-craft the password hash. It is
`pbkdf2-sha256$210000$<base64 salt>$<base64 hash>`, and a subtly wrong one is
indistinguishable from a wrong password at login — let the service hash it.

**1. Register.** Password must be 8-128 characters with at least one letter and
one digit.

```
curl -X POST "https://buildnexus-user-service-2026.azurewebsites.net/api/auth/register" \
  -H "Content-Type: application/json" \
  -d '{"fullName":"System Administrator","email":"admin@buildnexus.example","password":"<strong password>","role":"ProjectManager"}'
```

Expect `201`.

**2. Open MySQL to your own machine.** The `allow-azure-services` firewall rule
covers Azure services, not a laptop. This rule is intentionally not in Terraform:
it is tied to one machine's current IP and would go stale in the repository.
Terraform manages firewall rules as individual resources, so an extra one added
here is not reverted by a later apply.

```
MYIP=$(curl -s https://api.ipify.org)

az mysql flexible-server firewall-rule create \
  --resource-group buildnexus-rg --name buildnexus-mysql-2026 \
  --rule-name bootstrap-admin --start-ip-address "$MYIP" --end-ip-address "$MYIP"
```

**3. Promote.** Emails are stored trimmed and lower-cased, so match the
lower-case form. `--ssl-mode=REQUIRED` because the server refuses unencrypted
connections.

```
mysql --host buildnexus-mysql-2026.mysql.database.azure.com \
      --user buildnexusadmin -p --ssl-mode=REQUIRED --database buildnexus_user_db
```

```sql
UPDATE users SET role = 'Admin', updated_at = UTC_TIMESTAMP()
WHERE email = 'admin@buildnexus.example';

SELECT id, email, role, is_active FROM users WHERE role = 'Admin';
```

Exactly one row. A mistyped role is rejected by the `ck_users_role` check
constraint rather than stored.

**4. Log in again.** The `role` claim is built from the database row at login, so
a fresh login returns an Admin token. A token issued before the UPDATE keeps its
old role until it expires — 20 minutes, with no clock skew allowance.

**5. Close the firewall again.**

```
az mysql flexible-server firewall-rule delete \
  --resource-group buildnexus-rg --name buildnexus-mysql-2026 \
  --rule-name bootstrap-admin --yes
```

## Application settings

Every setting is applied by Terraform, in `app_settings` on the App Service in
`terraform/user-service.tf`. Nothing is set by hand in the portal — a hand-edit
is reverted by the next `terraform apply`, silently, which is a bad afternoon.

| Setting | Source |
|---|---|
| `ConnectionStrings__UserDb` | Built from the server and database resources. Carries `SslMode=Required`. |
| `Jwt__SigningKey` | `var.jwt_signing_key`. Sensitive, no default. |
| `Jwt__Issuer` / `Jwt__Audience` | `var.jwt_issuer` / `var.jwt_audience`. Shared by every service. |
| `Jwt__AccessTokenLifetimeMinutes` | Fixed at 20. User Service only — other services just read `exp`. |
| `InternalService__ApiKey` | `var.internal_service_api_key`. Sensitive, no default. |
| `PasswordReset__ResetUrlTemplate` | Built from `var.frontend_origin`. |
| `ASPNETCORE_ENVIRONMENT` | `Production`. Turns off Swagger and `AdminSeeder`. |

`Email__SmtpHost` is deliberately unset. Blank, the service resolves
`IEmailSender` to `LoggingEmailSender` and password reset emails go to the log
stream instead of being sent. There is no mail relay in this stack, and a
deployed environment mailing real addresses is not something to switch on by
accident. Mailpit is local-only and is not deployed.

## Known issue: TLS interception breaks the Terraform plugin handshake

`terraform validate`, `plan` or `apply` failing like this:

```
Error: Failed to load plugin schemas
... failed to retrieve schema from provider "registry.terraform.io/hashicorp/azurerm":
Plugin did not respond ...
```

with `TF_LOG=ERROR` showing:

```
tls: failed to verify certificate: x509: certificate signed by unknown authority
```

is the same problem `README.md` documents for `docker compose` builds:
antivirus HTTPS scanning intercepting TLS. Terraform talks to its provider
plugins over mutually-authenticated TLS on localhost, and an interceptor breaks
that handshake even though nothing leaves the machine.

Proper fix: exempt Terraform from your antivirus's HTTPS scanning. Workaround,
if you cannot:

```
$env:TF_DISABLE_PLUGIN_TLS = "1"
```

This disables encryption only on the local process-to-process channel between
`terraform.exe` and the provider plugin. It does not weaken anything that crosses
the network, and it does not affect what gets created in Azure.
