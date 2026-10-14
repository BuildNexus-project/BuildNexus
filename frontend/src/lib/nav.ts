import {
  ChartBar,
  ChartColumn,
  ChartNoAxesCombined,
  FolderKanban,
  HardHat,
  LayoutDashboard,
  Radar,
  SquarePlus,
  UserCog,
  Users,
  Wallet,
  type LucideIcon,
} from 'lucide-react'
import { generatePath, matchPath } from 'react-router-dom'

import type { Role } from './roles'

/** One place a signed-in user can go, as the header, footer and dashboard all show it. */
export type NavItem = {
  to: string
  label: string
  /** One sentence for the dashboard card — what the page is for, in the user's terms. */
  description: string
  icon: LucideIcon
}

export const DASHBOARD_ITEM: NavItem = {
  to: '/home',
  label: 'Dashboard',
  description: 'Your workspace at a glance.',
  icon: LayoutDashboard,
}

const PROJECTS: NavItem = {
  to: '/projects',
  label: 'Projects',
  description: 'Open a project to see its details, its team and every status change.',
  icon: FolderKanban,
}

const NEW_PROJECT: NavItem = {
  to: '/projects/new',
  label: 'New project',
  description: 'Describe the building you want and submit it for the company to pick up.',
  icon: SquarePlus,
}

const PROGRESS: NavItem = {
  to: '/progress',
  label: 'Progress',
  description: 'Follow the build of your projects, milestone by milestone.',
  icon: HardHat,
}

const MY_COSTS: NavItem = {
  to: '/my-costs',
  label: 'My costs',
  description: 'What each project is expected to cost, and what has been billed so far.',
  icon: Wallet,
}

const TEAM: NavItem = {
  to: '/directory',
  label: 'Project team',
  description: 'Find the architects and project managers available to work on a project.',
  icon: Users,
}

const USERS: NavItem = {
  to: '/admin/users',
  label: 'Users',
  description: 'Every account on the platform — edit a person’s details or withdraw their access.',
  icon: UserCog,
}

const OVERSIGHT: NavItem = {
  to: '/admin/oversight',
  label: 'Oversight',
  description: 'Every project on the platform, who is on it and which have stalled — with the reports.',
  icon: Radar,
}

const PROJECT_REPORT: NavItem = {
  to: '/admin/reports/project-status',
  label: 'Project report',
  description: 'Every project grouped by status — the whole pipeline at a glance.',
  icon: ChartBar,
}

const DESIGN_REPORT: NavItem = {
  to: '/admin/reports/design-approval',
  label: 'Design report',
  description: 'How long design approval takes, project by project.',
  icon: ChartColumn,
}

const BUILD_PAYMENT_REPORT: NavItem = {
  to: '/reports/construction-payment',
  label: 'Build & payment',
  description:
    'Build progress across the active projects, and what has been invoiced, collected and is still owed.',
  icon: ChartNoAxesCombined,
}

/**
 * What each role is offered, in the order it is shown. Mirrors the route guards
 * in `App` — a role is only ever invited into a page it can open. The guards and
 * the services behind them still decide the real answer; this only decides what
 * to put in front of somebody.
 */
export const NAV_ITEMS: Record<Role, readonly NavItem[]> = {
  Client: [PROJECTS, NEW_PROJECT, PROGRESS, MY_COSTS],
  Architect: [PROJECTS, TEAM],
  ProjectManager: [PROJECTS, TEAM, BUILD_PAYMENT_REPORT],
  Admin: [PROJECTS, OVERSIGHT, USERS, PROJECT_REPORT, DESIGN_REPORT, BUILD_PAYMENT_REPORT],
}

/** What a role calls its own corner of the app, shown beside the breadcrumbs. */
export const WORKSPACE_LABELS: Record<Role, string> = {
  Client: 'Client workspace',
  Architect: 'Architect workspace',
  ProjectManager: 'Project manager workspace',
  Admin: 'Admin workspace',
}

/**
 * The nav item a path belongs to, so the header can mark where the user is.
 *
 * The longest matching item wins: `/projects/new` is under `/projects`, but it is
 * "New project" that is current there, not "Projects". A path that no item
 * covers — the profile page, say — marks nothing.
 */
export function activeNavItem(role: Role, pathname: string): NavItem | undefined {
  const items = [DASHBOARD_ITEM, ...NAV_ITEMS[role]]

  return items
    .filter((item) => matchPath({ path: item.to, end: item.to === '/home' }, pathname) !== null)
    .sort((a, b) => b.to.length - a.to.length)
    .at(0)
}

/** One step of a breadcrumb trail. The last step is the page itself and has no link. */
export type Crumb = { label: string; to?: string }

/**
 * The trail to each page, most specific route first so `/projects/new` is not
 * swallowed by `/projects/:projectId`.
 *
 * Labels are generic on purpose ("Project", not the project's name): the trail is
 * built from the URL alone, and the page itself is what says which project it is.
 * A `to` may name route params, which are filled in from the current URL.
 */
const TRAILS: ReadonlyArray<{ path: string; trail: readonly Crumb[] }> = [
  { path: '/projects/new', trail: [{ label: 'Projects', to: '/projects' }, { label: 'New project' }] },
  {
    path: '/projects/:projectId/designs',
    trail: [
      { label: 'Projects', to: '/projects' },
      { label: 'Project', to: '/projects/:projectId' },
      { label: 'Design documents' },
    ],
  },
  {
    path: '/projects/:projectId/costs',
    trail: [
      { label: 'Projects', to: '/projects' },
      { label: 'Project', to: '/projects/:projectId' },
      { label: 'Costs' },
    ],
  },
  {
    path: '/projects/:projectId',
    trail: [{ label: 'Projects', to: '/projects' }, { label: 'Project' }],
  },
  { path: '/projects', trail: [{ label: 'Projects' }] },
  { path: '/progress', trail: [{ label: 'Construction progress' }] },
  { path: '/my-costs', trail: [{ label: 'My costs' }] },
  { path: '/directory', trail: [{ label: 'Project team' }] },
  { path: '/notifications', trail: [{ label: 'Notifications' }] },
  { path: '/profile', trail: [{ label: 'Profile' }] },
  { path: '/admin/users', trail: [{ label: 'Users' }] },
  { path: '/admin/oversight', trail: [{ label: 'Oversight' }] },
  {
    path: '/reports/construction-payment',
    trail: [{ label: 'Reports' }, { label: 'Construction & payment' }],
  },
  {
    path: '/admin/reports/design-approval',
    trail: [{ label: 'Reports' }, { label: 'Design approval' }],
  },
  {
    path: '/admin/reports/project-status',
    trail: [{ label: 'Reports' }, { label: 'Project status' }],
  },
]

/**
 * The breadcrumb trail for a path, after the implicit "Home". Empty for the
 * dashboard itself and for any path that has no trail.
 */
export function breadcrumbsFor(pathname: string): Crumb[] {
  for (const { path, trail } of TRAILS) {
    const match = matchPath({ path, end: true }, pathname)

    if (match) {
      return trail.map((crumb) =>
        crumb.to ? { ...crumb, to: generatePath(crumb.to, match.params) } : crumb,
      )
    }
  }

  return []
}
