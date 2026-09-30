Construction service README

## Role dashboards (US-21)

The construction slice of the Client's and the Project Manager's dashboards. The
other slices come from the other services; the page joins them on the project id
(see `frontend/README.md`, *Role dashboards*).

| Method | Route                                       | Allowed roles  |
|--------|---------------------------------------------|----------------|
| GET    | `/api/construction/dashboard/client`         | Client         |
| GET    | `/api/construction/dashboard/project-manager`| ProjectManager |

- **Client** — `{ projects[] }`: build progress for each project the caller owns
  that has milestones planned and has not been handed over, each
  `{ projectId, phaseStatus, totalMilestones, completedMilestones, progressPercent }`.
  Ownership is this service's own `project_owners` record, so no other service is
  asked, and the endpoint takes no id. A project that is planned but not started is
  listed with a `null` `phaseStatus` and `0` progress; one with no milestones is
  left out, since a zero would read as a stalled build.
- **Project Manager** — `{ activeBuildCount, activeBuilds[], milestonesDue }`.
  `activeBuilds` are the builds that have been started and not handed over
  (`Started` or `Completed`), each with its progress and `outstandingMilestones`.
  `milestonesDue` is `{ totalCount, milestones[] }`: the count of every milestone
  still to finish on those builds, and the first ten — those `InProgress` first,
  then `NotStarted`, each in the order they were planned. The count is never
  capped.

**"Due" means outstanding.** Milestones have no due date — US-12 defines only
`NotStarted`, `InProgress` and `Completed` — so there is nothing to be late
against. Adding one is a change to how milestones are created and belongs to that
story; it is recorded as a gap, not invented here.

**The Project Manager's view is portfolio-wide**, like the construction report.
This service records who owns a project but not which Project Manager runs it, so
it cannot narrow to "your" builds. Narrowing needs assignment data that only the
Project Service holds.

The progress percentage is calculated the same way as on the per-project screen
and in the report, so a project's figure cannot disagree between them. All three
reads are ADO.NET in `ConstructionDashboardRepository`; the phase and milestone
breakdown reuse `ConstructionProgressReportRow`.

`ConstructionDashboardEndpointTests` covers the mapping, empty results, caller
scoping, the list cap and the `401` path over a stand-in repository.
`ConstructionDashboardRepositoryDatabaseTests` proves the SQL against
`construction-db` — the ownership join, the "under way" gate, hand-over, ordering
and the capped list. `EndpointRoleDeclarationTests` pins each endpoint to its own
role.
