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
