import { describe, expect, it } from 'vitest'

import { initialsOf } from '@/lib/initials'
import { NAV_ITEMS, activeNavItem, breadcrumbsFor } from '@/lib/nav'
import {
  ADMIN_ROLES,
  CLIENT_ROLES,
  PROJECT_STAFF_ROLES,
  REPORTING_ROLES,
  ROLES,
  type Role,
} from '@/lib/roles'

describe('NAV_ITEMS', () => {
  /**
   * The route guards in `App` are the truth about who may open a page; the nav
   * is only what is put in front of somebody. If the two ever disagree a role is
   * invited into a screen that answers "you do not have access", so each guarded
   * destination is checked against the same role list the guard uses.
   */
  const GUARDS: Record<string, readonly Role[]> = {
    '/projects/new': CLIENT_ROLES,
    '/progress': CLIENT_ROLES,
    '/my-costs': CLIENT_ROLES,
    '/directory': PROJECT_STAFF_ROLES,
    '/admin/users': ADMIN_ROLES,
    '/admin/reports/design-approval': ADMIN_ROLES,
    '/admin/reports/project-status': ADMIN_ROLES,
    '/reports/construction-payment': REPORTING_ROLES,
  }

  it.each(ROLES)('only offers %s pages that role may open', (role) => {
    for (const item of NAV_ITEMS[role]) {
      const allowed = GUARDS[item.to]

      // A destination with no guard (Projects) is open to every signed-in role.
      if (allowed) {
        expect(allowed, `${role} is offered ${item.to}`).toContain(role)
      }
    }
  })

  it('offers every role the projects list', () => {
    for (const role of ROLES) {
      expect(NAV_ITEMS[role].map((item) => item.to)).toContain('/projects')
    }
  })

  it('offers each guarded page to every role that may open it', () => {
    // The other direction: a page a role can use but is never shown is a page
    // they cannot find.
    for (const [to, allowed] of Object.entries(GUARDS)) {
      for (const role of allowed) {
        expect(NAV_ITEMS[role].map((item) => item.to), `${role} should be offered ${to}`).toContain(
          to,
        )
      }
    }
  })

  it('gives every item its own destination within a role', () => {
    for (const role of ROLES) {
      const destinations = NAV_ITEMS[role].map((item) => item.to)

      expect(new Set(destinations).size).toBe(destinations.length)
    }
  })
})

describe('activeNavItem', () => {
  it('marks the dashboard on /home', () => {
    expect(activeNavItem('Client', '/home')?.label).toBe('Dashboard')
  })

  it('marks Projects for the list and for a single project', () => {
    expect(activeNavItem('Client', '/projects')?.label).toBe('Projects')
    expect(activeNavItem('Client', '/projects/6f9619ff-8b86-d011-b42d-00cf4fc964ff')?.label).toBe(
      'Projects',
    )
  })

  it('marks Projects for the pages that hang off a single project', () => {
    expect(activeNavItem('ProjectManager', '/projects/abc/costs')?.label).toBe('Projects')
    expect(activeNavItem('Architect', '/projects/abc/designs')?.label).toBe('Projects')
  })

  it('prefers the more specific item, so New project is current and Projects is not', () => {
    expect(activeNavItem('Client', '/projects/new')?.label).toBe('New project')
  })

  it('marks nothing on a page the nav does not list', () => {
    expect(activeNavItem('Client', '/profile')).toBeUndefined()
  })

  it('marks nothing on a page the role is not offered', () => {
    // An Architect who typed the URL of a Client page is shown the access
    // notice; the header should not pretend they are somewhere in their own nav.
    expect(activeNavItem('Architect', '/progress')).toBeUndefined()
  })
})

describe('breadcrumbsFor', () => {
  it('has no trail on the dashboard, which is the top of it', () => {
    expect(breadcrumbsFor('/home')).toEqual([])
  })

  it('has no trail for a path it does not know', () => {
    expect(breadcrumbsFor('/nowhere')).toEqual([])
  })

  it('ends a trail on the page itself, unlinked', () => {
    expect(breadcrumbsFor('/projects')).toEqual([{ label: 'Projects' }])
    expect(breadcrumbsFor('/profile')).toEqual([{ label: 'Profile' }])
  })

  it('links every step but the last', () => {
    expect(breadcrumbsFor('/projects/new')).toEqual([
      { label: 'Projects', to: '/projects' },
      { label: 'New project' },
    ])
  })

  it('does not mistake "new" for a project id', () => {
    // /projects/new also matches /projects/:projectId, so the order matters.
    expect(breadcrumbsFor('/projects/new').at(-1)?.label).toBe('New project')
  })

  it('links back to the project it sits under, with its own id filled in', () => {
    expect(breadcrumbsFor('/projects/abc-123/costs')).toEqual([
      { label: 'Projects', to: '/projects' },
      { label: 'Project', to: '/projects/abc-123' },
      { label: 'Costs' },
    ])

    expect(breadcrumbsFor('/projects/abc-123/designs').map((crumb) => crumb.label)).toEqual([
      'Projects',
      'Project',
      'Design documents',
    ])
  })

  it('leads to the combined report through Reports', () => {
    expect(breadcrumbsFor('/reports/construction-payment')).toEqual([
      { label: 'Reports' },
      { label: 'Construction & payment' },
    ])
  })

  it('leads to the project status report through Reports', () => {
    expect(breadcrumbsFor('/admin/reports/project-status')).toEqual([
      { label: 'Reports' },
      { label: 'Project status' },
    ])
  })

  it('reads a single project as Projects › Project', () => {
    expect(breadcrumbsFor('/projects/abc-123')).toEqual([
      { label: 'Projects', to: '/projects' },
      { label: 'Project' },
    ])
  })
})

describe('initialsOf', () => {
  it('takes the first and last word', () => {
    expect(initialsOf('Ada Perera')).toBe('AP')
    expect(initialsOf('Ada Lovelace Perera')).toBe('AP')
  })

  it('uses one letter for a single name', () => {
    expect(initialsOf('Madonna')).toBe('M')
  })

  it('upper-cases and ignores extra spacing', () => {
    expect(initialsOf('  ada   perera ')).toBe('AP')
  })

  it('does not fall over on an empty name', () => {
    expect(initialsOf('')).toBe('?')
    expect(initialsOf('   ')).toBe('?')
  })
})
