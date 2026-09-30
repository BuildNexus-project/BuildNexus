import type { ApiFetchOptions } from './api'
import type { ConstructionPhaseStatus, MilestoneStatus } from './construction-api'
import type { ProjectStatus } from './project-status'
import type { Role } from './roles'

/** The token-attaching fetch handed out by the auth context. */
type AuthFetch = <T>(path: string, options?: Omit<ApiFetchOptions, 'token'>) => Promise<T>

/*
 * The role dashboards (US-21).
 *
 * A dashboard needs data from all five services, and each service's database is its
 * own — so there is no single "dashboard" call. Every service exposes the slice it
 * owns, under its own gateway prefix, and the page joins the slices on the project id.
 * Each slice is typed and fetched here; how they are put together is the dashboard's
 * business, not this module's.
 *
 * Every endpoint is gated to exactly one role by its service, so a slice is only ever
 * asked for by the role it belongs to. None of them takes an id: who is asking decides
 * what comes back, and it is always the token's own account.
 */

// ---------------------------------------------------------------- Project Service ----

/** One project on a role dashboard — enough to recognise it and see where it stands. */
export type DashboardProject = {
  id: string
  name: string
  location: string
  status: ProjectStatus
  /** ISO-8601. When it last moved. */
  updatedAt: string
}

/** The Client's active projects (AC-1). Active is anything that is neither `Completed` nor `Cancelled`. */
export type ClientProjectsDashboard = {
  /** The length of `projects`. */
  activeCount: number
  /** Most recently moved first. */
  projects: DashboardProject[]
}

/** The active projects the Architect is assigned to (AC-2). */
export type ArchitectProjectsDashboard = {
  /** The length of `projects`. */
  assignedCount: number
  /** Most recently moved first. */
  projects: DashboardProject[]
}

/** The system-wide project count (AC-4): the total, and how it divides across the lifecycle. */
export type AdminProjectsDashboard = {
  /** Every project, whatever its status — the sum of `groups`. */
  totalCount: number
  /** One entry per status in lifecycle order, including statuses nothing is in. */
  groups: { status: ProjectStatus; count: number }[]
}

export function fetchClientProjectsDashboard(authFetch: AuthFetch) {
  return authFetch<ClientProjectsDashboard>('/api/projects/dashboard/client')
}

export function fetchArchitectProjectsDashboard(authFetch: AuthFetch) {
  return authFetch<ArchitectProjectsDashboard>('/api/projects/dashboard/architect')
}

export function fetchAdminProjectsDashboard(authFetch: AuthFetch) {
  return authFetch<AdminProjectsDashboard>('/api/projects/dashboard/admin')
}

// ------------------------------------------------------------------- User Service ----

/** The system-wide user counts (AC-4). */
export type AdminUsersDashboard = {
  /** Every account, active or not — the sum of `roles`. */
  totalUsers: number
  /** Accounts that can sign in. */
  activeUsers: number
  /** Accounts an Admin has withdrawn access from. Still counted in `totalUsers`. */
  inactiveUsers: number
  /** One entry per platform role in the platform's order, including roles nobody holds. */
  roles: { role: Role; count: number }[]
}

export function fetchAdminUsersDashboard(authFetch: AuthFetch) {
  return authFetch<AdminUsersDashboard>('/api/users/dashboard/admin')
}

// ------------------------------------------------------------------ Design Service ----

/**
 * Where a project's design stands as a whole, from the review state of each document's
 * *latest* version — exactly as the Design Service spells them.
 *
 * Listed most in need of the Client first: something waiting on their review outranks a
 * revision they are waiting on, which outranks everything being signed off. `NoDesign`
 * is a status in its own right, not an absence — nothing has been uploaded yet.
 */
export const DESIGN_STATES = ['NoDesign', 'AwaitingReview', 'RevisionRequested', 'Approved'] as const

export type DesignState = (typeof DESIGN_STATES)[number]

/** Display names, in the Client's own voice — the wire values read badly in a UI. */
export const DESIGN_STATE_LABELS: Record<DesignState, string> = {
  NoDesign: 'No design yet',
  AwaitingReview: 'Awaiting your review',
  RevisionRequested: 'Revision requested',
  Approved: 'Approved',
}

/** One active project's design status. The counts add up to `documentCount`. */
export type ProjectDesignStatus = {
  projectId: string
  state: DesignState
  documentCount: number
  /** Documents whose latest version is waiting on the Client's decision. */
  awaitingReviewCount: number
  /** Documents the Client sent back and the Architect has not answered. */
  revisionRequestedCount: number
  approvedCount: number
}

/** The design status of each of the Client's active projects (AC-1), in the Project Service's order. */
export type ClientDesignDashboard = {
  projects: ProjectDesignStatus[]
}

/** A revision a Client asked for that the Architect has not yet answered. */
export type PendingRevision = {
  projectId: string
  documentId: string
  /** The document's name — "GroundFloorPlan", "Elevations". */
  documentName: string
  /** The version the Client sent back for changes. */
  versionNumber: number
  /** What the Client asked to change. */
  reviewComment: string | null
  /** ISO-8601. When the Client asked. */
  requestedAt: string
}

/** The revisions the Architect still owes (AC-2), longest waiting first. */
export type ArchitectDesignDashboard = {
  /** The length of `revisions`. */
  pendingRevisionCount: number
  revisions: PendingRevision[]
}

/**
 * The design half of a Client's dashboard.
 *
 * The Design Service holds no record of who owns a project, so it asks the Project
 * Service — with the caller's own token — before answering. If that service cannot be
 * reached this fails with a 502 rather than reporting "no projects": an outage must not
 * read as an empty dashboard.
 */
export function fetchClientDesignDashboard(authFetch: AuthFetch) {
  return authFetch<ClientDesignDashboard>('/api/designs/dashboard/client')
}

/** The design half of an Architect's dashboard. Fails with a 502 for the same reason as {@link fetchClientDesignDashboard}. */
export function fetchArchitectDesignDashboard(authFetch: AuthFetch) {
  return authFetch<ArchitectDesignDashboard>('/api/designs/dashboard/architect')
}

// ------------------------------------------------------------- Construction Service ----

/**
 * How far along one of the Client's builds is.
 *
 * `phaseStatus` is `null` for a project whose build is planned but not started — a real
 * line, at zero progress, not a stalled build.
 */
export type ProjectBuildProgress = {
  projectId: string
  phaseStatus: ConstructionPhaseStatus | null
  totalMilestones: number
  completedMilestones: number
  /** Completed over total, to two decimal places. */
  progressPercent: number
}

/** The Client's build progress (AC-1): projects they own with milestones planned and not yet handed over. */
export type ClientConstructionDashboard = {
  projects: ProjectBuildProgress[]
}

/** One build under way. */
export type ActiveBuild = {
  projectId: string
  /** The project's name, as the Project Service gave it; `null` only if it somehow did not. */
  projectName: string | null
  /** `Started`, or `Completed` while it awaits handover. */
  phaseStatus: ConstructionPhaseStatus | null
  totalMilestones: number
  completedMilestones: number
  /** What is left on this build: the milestones that are not `Completed`. */
  outstandingMilestones: number
  progressPercent: number
}

/** One milestone still to finish on a running build. */
export type MilestoneDue = {
  id: string
  projectId: string
  /** The project's name, as the Project Service gave it. */
  projectName: string | null
  name: string
  /** Either `NotStarted` or `InProgress`. */
  status: MilestoneStatus
  /** The day it should be finished by, as `yyyy-MM-dd`, or `null` when none was set. */
  dueDate: string | null
  /** Whether `dueDate` is before today — the service's own judgement. `false` for no date. */
  isOverdue: boolean
  /** ISO-8601. */
  createdAt: string
}

/**
 * A Project Manager's dashboard (AC-3): the builds under way on the projects they are
 * assigned to, and the milestones still to finish on them.
 *
 * Scoped to the Project Manager's own projects. The Construction Service does not know who
 * runs a project, so it asks the Project Service — with the caller's own token — for the
 * projects they are assigned to; that same answer names them, so every build and milestone
 * says which project it is and no second request is needed. If the Project Service cannot be
 * reached this fails with a 502 rather than reporting "nothing under way".
 *
 * "Due" means outstanding: every milestone not yet completed. Where the Project Manager gave
 * a milestone a due date it can also be overdue; one with no date never is.
 */
export type ProjectManagerConstructionDashboard = {
  /** The length of `activeBuilds`. */
  activeBuildCount: number
  /** Most recently started first. */
  activeBuilds: ActiveBuild[]
  milestonesDue: {
    /** Every outstanding milestone on a running build — not the length of `milestones`. */
    totalCount: number
    /** How many of `totalCount` have a due date that has passed. Never counts an undated one. */
    overdueCount: number
    /** The first few: dated ones first, soonest (most overdue) first, then undated ones. */
    milestones: MilestoneDue[]
  }
}

export function fetchClientConstructionDashboard(authFetch: AuthFetch) {
  return authFetch<ClientConstructionDashboard>('/api/construction/dashboard/client')
}

export function fetchProjectManagerConstructionDashboard(authFetch: AuthFetch) {
  return authFetch<ProjectManagerConstructionDashboard>('/api/construction/dashboard/project-manager')
}

// --------------------------------------------------------------- Payment Service ----

/** One invoice the Client still owes money on. */
export type InvoiceDue = {
  invoiceId: string
  projectId: string
  /** The invoice's full amount. */
  amount: number
  /** What has been paid against it so far. */
  amountPaid: number
  /** What is still to pay — the figure the pay endpoint will accept up to. */
  outstandingAmount: number
  /** ISO-8601. When the invoice was raised. */
  raisedAt: string
}

/** What the Client still owes (AC-1), and on which invoices, oldest first. */
export type ClientPaymentsDashboard = {
  /** The sum of each invoice's outstanding amount. Zero when nothing is owing. */
  totalDue: number
  /** The length of `invoices`. */
  invoiceCount: number
  invoices: InvoiceDue[]
}

export function fetchClientPaymentsDashboard(authFetch: AuthFetch) {
  return authFetch<ClientPaymentsDashboard>('/api/payments/dashboard/client')
}
