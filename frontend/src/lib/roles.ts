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

/** Display names — the wire value `ProjectManager` reads badly in a UI. */
export const ROLE_LABELS: Record<Role, string> = {
  Client: 'Client',
  Architect: 'Architect',
  ProjectManager: 'Project Manager',
  Admin: 'Admin',
}
