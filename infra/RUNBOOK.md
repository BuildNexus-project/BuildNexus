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
| MySQL Flexible Server | `buildnexus-mysql-2026` | Shared. One *database* per service on it, each with its own scoped MySQL user rather than the administrator — Project Service onward; User Service still connects as the administrator (SCRUM-52 predates the convention). |
| User Service | `buildnexus-user-service-2026` | <https://buildnexus-user-service-2026.azurewebsites.net> |
| User Service database | `buildnexus_user_db` | On the shared server above. Connects as the server administrator. |
| Project Service | `buildnexus-project-service-2026` | <https://buildnexus-project-service-2026.azurewebsites.net> |
| Project Service database | `buildnexus_project_db` | On the shared server above. Connects as its own `project_service` MySQL user, scoped to this one database. |
| Event Hubs namespace | `buildnexus-events-2026` | Standard tier. The Kafka-compatible endpoint on `9093`, shared by every publishing service. |
| Event Hub `project-events` | `project-events` | Inside the namespace above — the Kafka topic Project Service publishes to. |
| Log Analytics workspace | `buildnexus-logs` | Backs the Application Insights resource below. Capped at `daily_quota_gb = 1`. |
| Application Insights | `buildnexus-appinsights` | Workspace-based. Wired into the User Service only, as SCRUM-47's proof of concept — see "Application settings" below. |

Event Hubs Standard bills per throughput unit per hour whether or not anything is
published to it. It is the first resource in this stack that charges while
completely idle, which is one more reason to actually run `terraform destroy`
between sessions rather than leaving the stack up "just in case".

The Log Analytics workspace and Application Insights are the opposite: both are
pure consumption resources, billed only on data ingested, with 5 GB free every
month. A resource that receives zero telemetry costs zero — there is no hourly
charge to avoid by destroying these two between sessions, though `terraform
destroy` still takes them with it along with everything else in the resource
group.

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

The location is written out as a literal on every resource in `terraform/main.tf`,
`terraform/user-service.tf` and `terraform/project-service.tf` so it cannot be
overridden back to a region that fails.

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

This module also uses two providers besides `azurerm`: `petoju/mysql`, which
signs in to the MySQL Flexible Server directly (not through Azure's management
API) to create each service's own scoped database user, and `hashicorp/time`,
used only for a fixed startup wait — see the known issue below. Both are
declared in `provider.tf` and `terraform init` downloads them automatically;
neither needs anything installed on the machine beyond Terraform itself.

Because the `mysql` provider connects directly, it has to reach the server past
its firewall from wherever Terraform is running — see `terraform_operator_ip`
below.

Finally the variables with no default — the two signing keys, the MySQL
administrator password, the Project Service's own database password, and the
machine's current public IP:

```
cd infra/terraform
cp terraform.tfvars.example terraform.tfvars    # then fill it in
```

`terraform.tfvars` is git-ignored. Generate fresh values with
`openssl rand -base64 48`; do **not** reuse the local-development values from
`infra/.env.example`, which are published in this repository on purpose. The
same `jwt_signing_key` must eventually be given to every other deployed service
and the API Gateway, or every token the User Service issues comes back 401.

`project_service_db_password` is a fresh password for Project Service's own
MySQL user, distinct from the administrator password and never reused as it.

`terraform_operator_ip` is **not a one-time value** — it is compared against
this machine's actual address on every apply, and Terraform has no way to
notice when it goes stale. Get the current one and put it in
`terraform.tfvars` before every apply, not just the first:

```
curl -4 -s https://api.ipify.org
```

The `-4` matters. Plain `curl -s` can report a different address than the one
the IPv4 path uses — see the known issue below.

If this differs from what is already in `terraform.tfvars`, a plan or apply that
has to reach `mysql_user` or `mysql_grant` times out connecting, and once those
exist in state a plain re-apply cannot fix it — see the known issue below
before assuming anything else is wrong.

### Apply

```
cd infra/terraform
terraform init      # first time in a working copy, or after changing provider.tf
terraform plan      # read this properly before applying
terraform apply
```

`terraform output` afterwards prints each service's public URL and App Service
name, the resource group, the MySQL FQDN, and the Event Hubs Kafka bootstrap
address. No secret is an output.

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
blob lease held but the lock metadata empty. During SCRUM-47 the first
`terraform plan` failed with `HTTP response was nil; connection may have been
reset` while acquiring the lock, and every attempt after it hit this error
until the lease was broken — a retry a few minutes later did not clear it. It
recurred later the same day from the other end: a `terraform plan` printed
`Error releasing the state lock` and advised `force-unlock`, and the next attempt
found the blob leased (`az storage blob show` reported `leaseState: leased`,
`leaseDuration: infinite`, empty metadata) with no `terraform.exe` running. A
failed release leaves exactly the same state as a failed acquire, so check for a
running Terraform first and then break the lease:

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

### Known issue: `mysql_user` or `mysql_grant` times out connecting

```
Error: failed to connect to MySQL: could not create new connection: could not
connect to server: dial tcp <ip>:3306: connectex: A connection attempt failed
because the connected party did not properly respond...
```

— even after Terraform has already waited out
`time_sleep.mysql_firewall_propagation` — looks like the firewall rule has not
taken effect yet. That has been the wrong diagnosis every time it has actually
happened. Check the IP first:

```
curl -4 -s https://api.ipify.org
```

against `terraform_operator_ip` in `terraform.tfvars` **and** against what the
server actually has (`az mysql flexible-server firewall-rule list -g buildnexus-rg
-n buildnexus-mysql-2026 -o table`). `terraform_operator_ip` is a snapshot of one
address on one network, not a live lookup, and Terraform has no way to notice it
changed. The rule allows exactly that one IPv4 address; from any other address
the connection never completes — a timeout, not a refusal, which is what
`did not properly respond` means.

#### Once `mysql_user` and `mysql_grant` exist in state, a plain re-apply cannot fix it

Terraform refreshes every resource before it plans, and refreshing an existing
`mysql_user` or `mysql_grant` opens a real MySQL connection. That needs the
firewall to already admit this machine — but the rule that would admit it is part
of the same plan. So the plan fails at the refresh, before it can propose the
rule change, let alone apply it. It is a deadlock, and retrying does not break
it.

The earlier advice here — update `terraform.tfvars` and re-apply — only works
while those two resources are being *created*, when there is nothing to refresh.
Seen during SCRUM-47, when the rule held the previous session's address: the same
configuration failed a plain `terraform plan` twice, then planned successfully with
`-refresh=false`, showing only the rule update and the `time_sleep` replacement.

To break it, update the rule through Terraform without refreshing, then go back
to ordinary plans. This is the full configuration, not `-target`:

```
curl -4 -s https://api.ipify.org          # put this in terraform.tfvars
cd infra/terraform
terraform plan -refresh=false -out=opip.tfplan
terraform apply opip.tfplan
rm opip.tfplan                            # the plan file holds sensitive values
terraform plan                            # an ordinary plan can refresh over MySQL again
```

Read the `-refresh=false` plan before applying it. It must show only
`azurerm_mysql_flexible_server_firewall_rule.terraform_operator` (update) and
`time_sleep.mysql_firewall_propagation` (replace). A plan built without refreshing
does not look at live state, so anything else in it is unverified — stop there.
Do not reach for `-target` instead: it leaves the operator-IP change unapplied and
the next full plan fails the same way.

Confirmed on 2026-09-19: after the rule was updated this way, a plain
`terraform plan` reported *No changes* and a plain `terraform apply` finished with
0 added, 0 changed, 0 destroyed, both refreshing `mysql_user` and `mysql_grant`
over live connections.

It failed again that evening, after the address had moved to `45.121.88.242`,
and the same steps fixed it: a plain plan failed with the identical MySQL timeout,
the `-refresh=false` plan showed only the two expected changes (the firewall rule
and the `time_sleep` replacement), it applied as 1 added, 1 changed, 1 destroyed,
and a plain `terraform plan -detailed-exitcode` then exited `0` — *No changes* —
refreshing `mysql_user` and `mysql_grant`.

#### If the error names an IPv6 address such as `[64:ff9b::…]:3306`

The address family is a red herring, but it explains the odd-looking error and
one wrong turn worth avoiding.

- **Where it comes from.** The server's address is the IPv4 `A` record,
  `20.195.37.236`. On the phone hotspot (`Pixel_4230`) name resolution also returns
  `64:ff9b::14c3:25ec`; `64:ff9b::/96` is the NAT64 prefix and its last 32 bits,
  `14c3:25ec`, are that same IPv4 address, so it is a synthesized `AAAA` (the
  network's DNS64 resolver is `2405:6b00:66:8bd1::9`). The machine also has global IPv6 there,
  and Windows' prefix policy ranks `::/0` (40) above IPv4 (35), so the IPv6 path
  is tried first. That is why the provider's error shows an IPv6 address.
- **It is not the cause.** The IPv4 path to the server failed identically. The
  network did not filter the port: TCP 3306 to a non-Azure test host
  (`portquiz.net`) connected over both the IPv4 and the NAT64 path, and TCP 443
  to Azure connected over IPv4 and over native IPv6 (NAT64 to Azure was not
  tested). What differed was the address the server's firewall saw, which is the
  most consistent explanation; the decisive check was that allowing the current
  IPv4 address made the connections succeed. Since the rule held a single IPv4 address and the connections that then
  succeeded can only have arrived from it, they must have come over the IPv4 path
  — consistent with the dialer trying IPv6 first and falling back to IPv4 when
  that attempt does not connect; the fallback itself was inferred, not observed.
- **Why plain `curl -s` gave the wrong value.** `api.ipify.org` is IPv4-only, so
  on this network curl takes the synthesized IPv6 address and leaves through the
  NAT64 gateway, reporting the *gateway's* address. Measured on 2026-09-19:
  `curl -4` gave `103.21.166.122` and later `103.21.165.15`, while the NAT64 path
  gave `45.121.89.223` and, a short while later, `45.121.89.193`. The value first
  put in `terraform.tfvars` this session came from plain `curl` and was
  `45.121.89.223` — the same as the NAT64 reading, so it was almost certainly a
  NAT64 address. Use `-4`.
- **There is no IPv6 rule to write instead.** The Azure CLI documents the
  firewall rule's start and end address as "Must be IPv4 format", and Microsoft's
  docs say an IPv6 rule fails validation. Traffic that arrives by NAT64 reaches the
  server as IPv4 from the gateway's pool, which is what an IPv4 rule matches — but
  that pool address changed between two requests made a short while apart, so a
  single-address rule cannot reliably cover it. The IPv4 path was steadier: six
  samples over about a minute returned the same address.

#### Expect the address to change between sessions on a phone hotspot

The rule that worked on 2026-09-15 held `103.21.164.44`. On 2026-09-19 the same
SSID gave four different IPv4-path addresses in one day: `103.21.166.122`, then
`103.21.165.15`, then `103.21.166.49`, then, that evening, `45.121.88.242`. The
first three share `103.21.164.x`–`103.21.166.x`, but the last is in a different
range, and `45.121.88.x`/`45.121.89.x` was where the NAT64 path had appeared
earlier — so it is not one tidy pool, and an address's range cannot be relied on
to tell you which path it came through. The Windows log recorded five Wi-Fi
connection events between 10:20 and 10:58 that morning; whether each one produced
a new address was not tested. The machine also joins `SLIIT-STD`; that network's
public address was not measured, so it is not known to be steadier.

Treat `terraform_operator_ip` as something to re-measure at the start of every
session on a hotspot, and do the `-refresh=false` step above whenever it differs
from the rule. Widening the rule to cover the churn is not workable: no single
range spans the addresses seen, and any range wide enough to try would admit
hundreds of unrelated subscribers' addresses to the database's public endpoint
with only a password and TLS in front of it. That was not done and is not
recommended.

`time_sleep.mysql_firewall_propagation` (in `main.tf`) exists for a *different*,
still-possible failure — Azure's own documented lag between a firewall rule
reading back as created and the server actually enforcing it, which cannot be
detected in advance and so is guarded with a fixed pause rather than left
unguarded. It simply was not what caused this failure the one time it was seen
during this story: the rule had existed for hours, the wait had already
elapsed, and only the stale IP explained it.

### Known issue: a manual zip deploy fails with an unhelpful 400

`az webapp deploy --type zip` returning

```
An error occurred during deployment. Status Code: 400
```

says almost nothing. The real reason is in the deployment log the error points
at:

```
az webapp log deployment show --resource-group buildnexus-rg --name <app-name>
```

If that log shows something like `rsync: [generator] recv_generator: failed to
stat ".../runtimes\win-x64\native\....dll": Invalid argument (22)`, the zip
itself is malformed: **Windows PowerShell's `Compress-Archive` writes `\` as
the path separator inside the archive**, which the ZIP format does not allow.
A nested folder arrives on the Linux-based App Service as one file with a
literal backslash in its name instead of a directory, and the deploy pipeline's
rsync step refuses to write it.

It surfaces for Project Service (and will for Design, Construction and Payment)
rather than for User Service because `Confluent.Kafka` ships native `librdkafka`
binaries for seven platforms under `runtimes/<rid>/native/` in a default
publish — the first folder structure nested deep enough to trigger it. Any
service that references `Confluent.Kafka` is affected the same way.

Build the zip with something that writes forward slashes instead. Git Bash's
`zip -r`, already used in the redeploy recipes below, does this correctly:

```
cd publish && zip -r ../publish.zip . && cd ..
```

If `zip` is not installed, Python's `zipfile` module does the same thing
explicitly:

```python
import zipfile, os
with zipfile.ZipFile("publish.zip", "w", zipfile.ZIP_DEFLATED) as zf:
    for root, dirs, files in os.walk("publish"):
        for name in files:
            full = os.path.join(root, name)
            zf.write(full, os.path.relpath(full, "publish").replace(os.sep, "/"))
```

Do not use `Compress-Archive` for any App Service zip deploy in this stack.

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

1. **The database contents.** The MySQL server is new and empty, for every
   service's database on it. DbUp recreates each schema at its own service's
   first startup, so the tables come back — the rows do not.
2. **The bootstrap Admin.** A consequence of (1). See below; there is no Admin
   account until someone makes one.
3. **Each service's publish profile.** App Service regenerates its deployment
   credentials when it is recreated, so both `AZURE_WEBAPP_PUBLISH_PROFILE_USER_SERVICE`
   and `AZURE_WEBAPP_PUBLISH_PROFILE_PROJECT_SERVICE` are stale, and the matching
   deploy job fails with a 401 that does not explain itself. Refresh each one:

   ```
   az webapp deployment list-publishing-profiles \
     --resource-group buildnexus-rg \
     --name <app-name> \
     --xml > publish-profile.xml
   ```

   using `buildnexus-user-service-2026` or `buildnexus-project-service-2026` for
   `<app-name>`. Paste the whole file into GitHub → Settings → Secrets and
   variables → Actions → the matching secret name above, then delete the local
   copy each time. It is a credential, and `*.xml` is not git-ignored.

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

## Redeploying Project Service

### The normal path

Push a change under `services/project-service/**` to `main`. The
`deploy-project-service` job in `.github/workflows/ci.yml` runs after the unit
test stage, publishes that one project, pushes it to its own App Service, and
polls `/health` until it answers 200 — the same shape as
`deploy-user-service`, in its own job rather than combined with it. Neither
deploy job depends on the other, and each has its own concurrency group, so a
Project Service deploy never rebuilds, restarts, or even waits behind a User
Service one, and the reverse.

A change anywhere else does not deploy the Project Service — the job is
filtered on that path and skipped otherwise.

### Forcing a redeploy without a code change

Same idea as the User Service, using this service's own project and app name:

```
dotnet publish services/project-service/ProjectService.csproj -c Release -o ./publish
cd publish && zip -r ../project-service.zip . && cd ..

az webapp deploy --resource-group buildnexus-rg \
  --name buildnexus-project-service-2026 \
  --src-path project-service.zip --type zip
```

**On Windows, build the zip with Git Bash's `zip -r` as above, not PowerShell's
`Compress-Archive`.** This service is the one most likely to hit the known
issue further up this document — Confluent.Kafka's native libraries are what
push the archive past the folder depth where `Compress-Archive` breaks.

To restart the process without deploying anything at all:

```
az webapp restart --resource-group buildnexus-rg --name buildnexus-project-service-2026
```

### Rolling back

Same constraints as the User Service — no deployment slots (Standard tier or
higher is needed and the plan is B1), no container image to re-tag. The same
three options, in the same order of preference: revert the commit and push,
re-run the last green deploy job as a stopgap, or deploy an older commit by hand
in an emergency and follow it with a revert.

### When a deploy goes green but the service does not answer

```
az webapp log tail --resource-group buildnexus-rg --name buildnexus-project-service-2026
```

The app validates its configuration at startup and refuses to boot on anything
missing or inconsistent: `Jwt__SigningKey` under 32 bytes, a connection string
that cannot reach MySQL as the `project_service` user, `Kafka__BootstrapServers`
absent, or a broker security setting configured only halfway
(`Kafka__SecurityProtocol` without all three of `Kafka__SaslMechanism` /
`Kafka__SaslUsername` / `Kafka__SaslPassword`, or the reverse). All of these
surface here as a startup exception rather than as a failing request.

**`/health` answering 200 does not prove Event Hubs is reachable.** It proves
the app started and migrated `buildnexus_project_db` as the scoped
`project_service` user — the Kafka producer only makes a real connection
attempt when the outbox dispatcher has an event to send. A wrong
`Kafka__SaslPassword` (a stale connection string after the namespace's keys were
regenerated, for instance) shows up only as a failed publish, never as a failed
startup. Confirm the whole path by creating or approving a project and tailing
the log for either a `Published <EventType> <eventId> ... to project-events`
line or a logged Kafka exception in its place.

## Health checks across services (SCRUM-47)

SCRUM-47's first acceptance criterion is that each service exposes a
health-check endpoint. "Each service" here means every service that currently
exists as running code — Azure for the two already deployed there, local
`docker compose` for the two that exist only there. Verified directly rather
than read off the source:

| Service | `/health` | Verified |
|---|---|---|
| User Service | Yes | Azure — already required by its own App Service health check (`terraform/user-service.tf`), and polled by `deploy-user-service` in CI on every deploy. Also confirmed live during this story, after the App Service was started and redeployed: `GET /health` → `200`, and 17 such requests are recorded in Application Insights (9 sent by hand; the other 8 are attributed to App Service's own health check). |
| Project Service | Yes | Azure — same arrangement, `terraform/project-service.tf` and `deploy-project-service`. **Not probed on Azure:** the App Service was found stopped (`403 - This web app is stopped`) and was left that way. Instead run natively (`dotnet run`) against `project-db` from `docker compose`: `curl http://localhost:5102/health` → `200 {"service":"project-service","status":"healthy"}`. That proves the endpoint answers; it does not prove the deployed instance does. |
| Design Service | Yes | Not deployed to Azure yet. Run natively (`dotnet run`) against `design-db` from `docker compose`: `curl http://localhost:5103/health` → `200 {"service":"design-service","status":"healthy"}`. |
| Construction Service | Yes | Not deployed to Azure yet. Run natively against `construction-db` from `docker compose`: `curl http://localhost:5104/health` → `200 {"service":"construction-service","status":"healthy"}`. |

The three native checks were repeated on the final commit of the story. Only the
databases came from `docker compose`; the services themselves were not run as
compose containers, because building those images failed on a network pull during
this story. So this verifies the endpoint in each service's code, not its
Dockerfile.
| Payment Service | Out of scope | `services/payment-service/` has no ASP.NET Core project — no `.csproj`, no `Program.cs`, not even an entry in `docker-compose.yml` — only a placeholder `README.md`. There is no host to add an endpoint to yet; the API Gateway's `payment` cluster route answers `502` until the story that builds this service lands. Revisit this row then. |

## Verifying the Application Insights proof of concept (SCRUM-47)

Application Insights only shows data once three separate things are all true:
the resource exists (`terraform apply` has run since `main.tf` added it), the
User Service is actually running, and it has been redeployed with the code
that reads `APPLICATIONINSIGHTS_CONNECTION_STRING`. None of the three implies
the others.

**Check the App Service is actually running first.** Both App Services were
found stopped when SCRUM-47 started: `curl` on `/health` answered `403 - This
web app is stopped`, which is the platform's own page for a stopped app, not a
connection failure or a missing endpoint. A stopped app serves nothing, so it
produces no telemetry. Start it:

```
az webapp start --resource-group buildnexus-rg --name buildnexus-user-service-2026
```

**1. Apply.** From a machine with `az login` and `ARM_ACCESS_KEY` set (see
"One-time setup per machine" above):

```
cd infra/terraform
terraform apply
```

This creates `buildnexus-logs` and `buildnexus-appinsights` and adds
`APPLICATIONINSIGHTS_CONNECTION_STRING` to the User Service's app settings. On
its own it does not put the SDK on the running app — that is the code this
story added, and the app is still running whatever it was last deployed with
until the next step.

During SCRUM-47 a plain `terraform plan` failed refreshing the existing
`mysql_user.project_service` with a connection timeout to an IPv6 address
(`[64:ff9b::14c3:25ec]:3306`), because the firewall rule still held the previous
session's address. The full explanation and the fix are under "Known issue:
`mysql_user` or `mysql_grant` times out connecting" above — set
`terraform_operator_ip` from `curl -4 -s https://api.ipify.org`, and if the rule
is out of date, update it with `terraform plan -refresh=false` / `terraform apply`
first.

The monitoring resources were first applied with `-target` while that was still
undiagnosed. That is not the procedure: it left the operator-IP change
unapplied, which was then reconciled with a full, un-targeted apply as described
there.

**2. Redeploy the User Service** with this branch's code, so the running
process actually contains `UseAzureMonitor()` — see "Forcing a redeploy
without a code change" above for the `dotnet publish` / `zip` /
`az webapp deploy` sequence.

**3. Generate real traffic.** A mix of successful and failing requests is
enough for a proof of concept. Requests that need no account avoid writing test
data into the shared database:

```
curl https://buildnexus-user-service-2026.azurewebsites.net/health
curl -X POST https://buildnexus-user-service-2026.azurewebsites.net/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"someone@example.invalid","password":"wrong-password"}'
```

The first gives `200`, the second `401`, and posting `{}` to the login route
gives `400`. Logging in with a real account's password should add a `200` to
that route; that was not tried here.

**4. Read it back.** In the Portal: the Application Insights resource →
Investigate → Failures / Performance, or Transaction search for individual
requests.

From the CLI, `az rest` needs no extension. `az monitor log-analytics query`
does: in a non-interactive shell it stops at an install prompt and fails with
`EOFError`, which reads as "no data" if a script swallows the error.
(`az monitor app-insights query` was not tried; it is likely to behave the same
way, since that command group is also an extension.) Workspace-based
Application Insights stores requests in the workspace's `AppRequests` table:

```
WS=$(az monitor log-analytics workspace show --resource-group buildnexus-rg \
       --workspace-name buildnexus-logs --query customerId -o tsv)

printf '{"query":"%s"}' 'AppRequests | where TimeGenerated > ago(1h) | summarize Requests=count() by Name, ResultCode, Success | order by Requests desc' > q.json

az rest --method post --url "https://api.loganalytics.io/v1/workspaces/$WS/query" \
  --resource "https://api.loganalytics.io" --body @q.json --query "tables[0].rows" -o json
```

(`workspace show` itself does not need the extension.) Telemetry usually takes
one to two minutes to appear after the request that generated it — an empty
result immediately after step 3 is not yet a failure.

### What the proof of concept showed

Recorded from the workspace after the steps above, for the User Service.
`AppRequests` for the period, by route and status:

| Route | Status | Requests | Source |
|---|---|---|---|
| `GET /health` | `200` | 17 | 9 sent by hand; the other 8 were not, and are attributed to App Service's health check (`health_check_path`) by subtraction |
| `POST api/auth/login` | `401` | 6 | wrong credentials, sent by hand |
| `POST api/auth/login` | `400` | 3 | malformed body, sent by hand |
| `GET /api/does-not-exist` | `401` | 3 | sent by hand — see below |
| `GET /` | `401` | 2 | not sent by hand; presumably the platform's warm-up probe |
| `GET /robots933456.txt` | `401` | 1 | not sent by hand; presumably the platform's warm-up probe |

Things worth knowing when reading it:

- **The `401` and `400` responses were recorded with `Success = false`**, so
  they appear as failed requests, not only 5xx would. Only these two codes were
  observed; nothing was sent that would return a 404 or a 5xx.
- **An unknown route answered `401`, not `404`.** That is consistent with the
  service's deny-by-default `FallbackPolicy` in `Program.cs`; the mechanism was
  not investigated further.
- **`GET /` `401` keeps accruing, and each one counts as a failed request.** A
  later count found 55 of them over roughly four and a half hours — about one
  every five minutes — alongside 287 `GET /health` `200`s. That cadence is
  consistent with App Service's Always On ping (`always_on = true`) hitting a
  route the deny-by-default policy rejects; it was not confirmed as the source.
  Either way, the Failures view carries a steady trickle of platform noise next
  to any real errors, so filter on `Name` when reading it.
- **No `AppDependencies` or `AppExceptions` rows were produced.** The SDK
  auto-instruments ASP.NET Core and outbound `HttpClient`, but not
  `MySqlConnector`, so the database calls under a request are not traced.
  Getting them would need explicit instrumentation — a further application
  change that SCRUM-47's acceptance criteria do not ask for, and not made here.
- `AppTraces` (existing `ILogger` output), `AppMetrics` and
  `AppPerformanceCounters` also received rows, so logs are centralized too.

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
MYIP=$(curl -4 -s https://api.ipify.org)   # -4: see "Known issue: mysql_user or mysql_grant times out connecting"

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

Every setting is applied by Terraform, in `app_settings` on each service's App
Service. Nothing is set by hand in the portal — a hand-edit is reverted by the
next `terraform apply`, silently, which is a bad afternoon.

### User Service (`terraform/user-service.tf`)

| Setting | Source |
|---|---|
| `ConnectionStrings__UserDb` | Built from the server and database resources. Carries `SslMode=Required`. |
| `Jwt__SigningKey` | `var.jwt_signing_key`. Sensitive, no default. |
| `Jwt__Issuer` / `Jwt__Audience` | `var.jwt_issuer` / `var.jwt_audience`. Shared by every service. |
| `Jwt__AccessTokenLifetimeMinutes` | Fixed at 20. User Service only — other services just read `exp`. |
| `InternalService__ApiKey` | `var.internal_service_api_key`. Sensitive, no default. |
| `PasswordReset__ResetUrlTemplate` | Built from `var.frontend_origin`. |
| `ASPNETCORE_ENVIRONMENT` | `Production`. Turns off Swagger and `AdminSeeder`. |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | `azurerm_application_insights.main.connection_string` (SCRUM-47). Read automatically by `UseAzureMonitor()` in `Program.cs` under this exact name — no custom configuration key. Every other environment leaves it unset, which is what tells `Program.cs` to skip registering Azure Monitor there instead of throwing at startup with nothing to send telemetry to. |

`Email__SmtpHost` is deliberately unset. Blank, the service resolves
`IEmailSender` to `LoggingEmailSender` and password reset emails go to the log
stream instead of being sent. There is no mail relay in this stack, and a
deployed environment mailing real addresses is not something to switch on by
accident. Mailpit is local-only and is not deployed.

### Project Service (`terraform/project-service.tf`)

| Setting | Source |
|---|---|
| `ConnectionStrings__ProjectDb` | Built from the server and database resources, using the scoped `project_service` MySQL user and `var.project_service_db_password` — never the server administrator. Carries `SslMode=Required`. |
| `Jwt__SigningKey` / `Jwt__Issuer` / `Jwt__Audience` | The same shared variables as every other service. |
| `Kafka__BootstrapServers` | The Event Hubs namespace's Kafka endpoint, `<namespace>.servicebus.windows.net:9093`. |
| `Kafka__SecurityProtocol` / `Kafka__SaslMechanism` / `Kafka__SaslUsername` / `Kafka__SaslPassword` | `SaslSsl` / `Plain` / the literal `$ConnectionString` / the namespace's `default_primary_connection_string`, read straight from the resource so it is never typed anywhere. |
| `Kafka__RequestTimeoutMs` / `Kafka__SocketKeepaliveEnable` / `Kafka__MetadataMaxAgeMs` | `60000` / `true` / `180000` — the values Azure documents for librdkafka clients connecting to Event Hubs. |
| `Kafka__MessageTimeoutMs` | `60000`, raised from the service's own 5000ms default so a slower Event Hubs acknowledgement is not counted as a failed publish. |
| `Services__UserService__BaseUrl` | The deployed User Service's own URL. Without it, assigning an Architect or Project Manager answers `502`. |
| `ASPNETCORE_ENVIRONMENT` | `Production`. Turns off Swagger. |

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
