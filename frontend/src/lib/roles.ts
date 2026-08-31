/** The four BuildNexus platform roles, exactly as the backend spells them. */
export const ROLES = ['Client', 'Architect', 'ProjectManager', 'Admin'] as const

export type Role = (typeof ROLES)[number]

/**
 * Roles a visitor may choose when registering.
 *
 * `Admin` is deliberately absent: the User Service refuses a self-assigned
 * Admin role with a 400, so offering it here would only produce a request we
 * know will be rejected.
 */
export const SELECTABLE_ROLES = ['Client', 'Architect', 'ProjectManager'] as const

export type SelectableRole = (typeof SELECTABLE_ROLES)[number]

/**
 * The two roles that staff and deliver a project, mirroring the User Service's
 * `PlatformRoles.ProjectStaff`. A Client is outside it — a customer has no
 * business browsing the firm's staff — and so is an Admin, who administers
 * accounts rather than taking part in project work.
 */
export const PROJECT_STAFF_ROLES: readonly Role[] = ['Architect', 'ProjectManager']

/** Account administration is Admin only, mirroring the service. */
export const ADMIN_ROLES: readonly Role[] = ['Admin']

/**
 * Submitting a construction project is the customer's job, mirroring the
 * Project Service's own gate on `POST /api/projects`. Staff roles do not
 * submit work on a Client's behalf, and an Admin administers accounts rather
 * than commissioning buildings.
 */
export const CLIENT_ROLES: readonly Role[] = ['Client']

/**
 * The roles that move a project through its lifecycle, mirroring the Project
 * Service's own gate on `PATCH /api/projects/{id}/status`.
 *
 * A Client is outside it on purpose: they can see every step of their project,
 * but declaring the design approved or the build finished is the company's
 * word, not the customer's. The service still refuses them whatever this does,
 * and it also checks the caller is actually on the project — a role is not
 * enough.
 */
export const STATUS_CHANGE_ROLES: readonly Role[] = ['Architect', 'ProjectManager', 'Admin']

/** Display names — the wire value `ProjectManager` reads badly in a UI. */
export const ROLE_LABELS: Record<Role, string> = {
  Client: 'Client',
  Architect: 'Architect',
  ProjectManager: 'Project Manager',
  Admin: 'Admin',
}
