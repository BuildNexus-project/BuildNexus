# Payment Service

Owns quotations and invoices: what a project is expected to cost, and what has
actually been billed against it (US-15).

- **Database** — `buildnexus_payment_db`, MySQL, on host port 3311. Owned
  exclusively by this service. No other service queries it or holds a foreign
  key into it; a project id is a plain column copied off an event or a request,
  never a key across a service boundary.
- **Schema** — applied at startup by DbUp from `Migrations/`, the same way every
  other BuildNexus service does it. Scripts run once, in filename order, and are
  recorded in `schemaversions` — never edit one that has shipped, add the next
  number instead.
- **Data access** — ADO.NET with direct SQL only. No ORM.
- **Auth** — validates the User Service's tokens under the shared convention
  (issuer `BuildNexusAuth`, audience `BuildNexusServices`, short `role` claim)
  and enforces its own role checks. Deny by default: an endpoint that declares
  no policy still requires an authenticated caller.
- **HTTP** — reached through the gateway at `/api/payments/**`, which already
  routes to this service's `payment` cluster.

Run it locally with `dotnet run` (port 5005) or through
`infra/docker-compose.yml`.

## Client dashboard (US-21)

The payments slice of the Client's dashboard: what they still owe. The other
slices come from the other services; the page joins them on the project id (see
`frontend/README.md`, *Role dashboards*).

| Method | Route                            | Allowed roles |
|--------|----------------------------------|---------------|
| GET    | `/api/payments/dashboard/client` | Client        |

Answers `{ totalDue, invoiceCount, invoices[] }`. Each invoice is
`{ invoiceId, projectId, amount, amountPaid, outstandingAmount, raisedAt }`,
oldest first. Only `Pending` invoices on projects the caller owns are listed — a
settled invoice is not due — and `totalDue` is the sum of what is still
**outstanding** on them, not what was billed.

Ownership is this service's own `project_owners` record, and the endpoint takes
no id, so a Client cannot ask for another Client's dashboard. An invoice on a
project with no recorded owner is shown to nobody.

"Outstanding" is the same definition US-16 uses when it accepts a payment and
US-17 uses on the billing view — the invoice's amount minus the sum of its
payments — so a figure on the dashboard is one the pay endpoint will accept. It
is one ADO.NET query (`PaymentDashboardRepository`) grouped by invoice, so an
invoice with several payments is counted once and one with none stays in at zero
paid.

`PaymentDashboardEndpointTests` covers the total, the mapping, ordering and the
empty case over a stand-in repository. `PaymentDashboardRepositoryDatabaseTests`
proves the SQL against `payment-db`, including that its outstanding figure agrees
with the billing history's. `EndpointRoleDeclarationTests` pins the endpoint to
Client.
