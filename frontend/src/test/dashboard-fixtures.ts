import type {
  AdminProjectsDashboard,
  AdminUsersDashboard,
  ArchitectDesignDashboard,
  ArchitectProjectsDashboard,
  ClientConstructionDashboard,
  ClientDesignDashboard,
  ClientPaymentsDashboard,
  ClientProjectsDashboard,
  DashboardProject,
  ProjectManagerConstructionDashboard,
} from '@/lib/dashboard-api'
import { apiResponse } from '@/test/fake-fetch'

/*
 * Two projects and the slices that describe them, in the shape each service answers with.
 * The dashboards join slices from different services on the project id, so tests need
 * the same two ids to appear in every slice — held here so they cannot drift apart.
 */

export const VILLA_ID = '11111111-2222-4333-8444-555555555555'
export const COTTAGE_ID = '66666666-7777-4888-8999-000000000000'

export const VILLA: DashboardProject = {
  id: VILLA_ID,
  name: 'Beachfront villa',
  location: 'Galle',
  status: 'Designing',
  updatedAt: '2026-09-04T09:00:00',
}

export const COTTAGE: DashboardProject = {
  id: COTTAGE_ID,
  name: 'Hilltop cottage',
  location: 'Kandy',
  status: 'Construction',
  updatedAt: '2026-09-02T09:00:00',
}

// ---------------------------------------------------------------- Client slices ----

export function clientProjects(projects: DashboardProject[] = [VILLA, COTTAGE]): ClientProjectsDashboard {
  return { activeCount: projects.length, projects }
}

/** The villa has a design waiting on the Client; the cottage's is signed off. */
export function clientDesign(): ClientDesignDashboard {
  return {
    projects: [
      {
        projectId: VILLA_ID,
        state: 'AwaitingReview',
        documentCount: 2,
        awaitingReviewCount: 1,
        revisionRequestedCount: 0,
        approvedCount: 1,
      },
      {
        projectId: COTTAGE_ID,
        state: 'Approved',
        documentCount: 1,
        awaitingReviewCount: 0,
        revisionRequestedCount: 0,
        approvedCount: 1,
      },
    ],
  }
}

/** Only the cottage has a build plan — three of its five milestones are done. */
export function clientConstruction(): ClientConstructionDashboard {
  return {
    projects: [
      {
        projectId: COTTAGE_ID,
        phaseStatus: 'Started',
        totalMilestones: 5,
        completedMilestones: 3,
        progressPercent: 60,
      },
    ],
  }
}

/** The cottage's invoice is 1,000.00 with 250.00 paid, so 750.00 is still owed. */
export function clientPayments(): ClientPaymentsDashboard {
  return {
    totalDue: 750,
    invoiceCount: 1,
    invoices: [
      {
        invoiceId: 'aaaaaaaa-0000-4000-8000-000000000001',
        projectId: COTTAGE_ID,
        amount: 1000,
        amountPaid: 250,
        outstandingAmount: 750,
        raisedAt: '2026-09-01T09:00:00',
      },
    ],
  }
}

export const CLIENT_PATHS = {
  projects: '/api/projects/dashboard/client',
  design: '/api/designs/dashboard/client',
  construction: '/api/construction/dashboard/client',
  payments: '/api/payments/dashboard/client',
} as const

/** Every Client slice answering 200 with the fixtures above. Override one to change what it says. */
export function clientRoutes(
  overrides: Partial<Record<keyof typeof CLIENT_PATHS, Response | Error>> = {},
): Record<string, Response | Error> {
  return {
    [CLIENT_PATHS.projects]: overrides.projects ?? apiResponse(200, clientProjects()),
    [CLIENT_PATHS.design]: overrides.design ?? apiResponse(200, clientDesign()),
    [CLIENT_PATHS.construction]: overrides.construction ?? apiResponse(200, clientConstruction()),
    [CLIENT_PATHS.payments]: overrides.payments ?? apiResponse(200, clientPayments()),
  }
}

// -------------------------------------------------------------- Architect slices ----

export function architectProjects(projects: DashboardProject[] = [VILLA, COTTAGE]): ArchitectProjectsDashboard {
  return { assignedCount: projects.length, projects }
}

/** The villa has two revisions waiting, the older first; the cottage has none. */
export function architectDesign(): ArchitectDesignDashboard {
  return {
    pendingRevisionCount: 2,
    revisions: [
      {
        projectId: VILLA_ID,
        documentId: 'dddddddd-0000-4000-8000-000000000001',
        documentName: 'GroundFloorPlan',
        versionNumber: 1,
        reviewComment: 'Move the stairs to the east wall',
        requestedAt: '2026-08-20T09:00:00',
      },
      {
        projectId: VILLA_ID,
        documentId: 'dddddddd-0000-4000-8000-000000000002',
        documentName: 'Elevations',
        versionNumber: 3,
        reviewComment: null,
        requestedAt: '2026-09-01T09:00:00',
      },
    ],
  }
}

export const ARCHITECT_PATHS = {
  projects: '/api/projects/dashboard/architect',
  design: '/api/designs/dashboard/architect',
} as const

export function architectRoutes(
  overrides: Partial<Record<keyof typeof ARCHITECT_PATHS, Response | Error>> = {},
): Record<string, Response | Error> {
  return {
    [ARCHITECT_PATHS.projects]: overrides.projects ?? apiResponse(200, architectProjects()),
    [ARCHITECT_PATHS.design]: overrides.design ?? apiResponse(200, architectDesign()),
  }
}

// ------------------------------------------------------- Project Manager slices ----

/**
 * Two builds under way on the Project Manager's projects — the cottage, started, and the
 * villa, finished and awaiting handover — and three milestones still to finish out of
 * fourteen, one of them overdue.
 *
 * The Construction Service names every project itself (from the Project Service's answer),
 * so there is no separate list of names: this is the whole of what the page is given.
 */
export function pmConstruction(): ProjectManagerConstructionDashboard {
  return {
    activeBuildCount: 2,
    activeBuilds: [
      {
        projectId: COTTAGE_ID,
        projectName: COTTAGE.name,
        phaseStatus: 'Started',
        totalMilestones: 5,
        completedMilestones: 2,
        outstandingMilestones: 3,
        progressPercent: 40,
      },
      {
        projectId: VILLA_ID,
        projectName: VILLA.name,
        phaseStatus: 'Completed',
        totalMilestones: 3,
        completedMilestones: 3,
        outstandingMilestones: 0,
        progressPercent: 100,
      },
    ],
    milestonesDue: {
      totalCount: 14,
      overdueCount: 1,
      milestones: [
        {
          id: 'eeeeeeee-0000-4000-8000-000000000001',
          projectId: COTTAGE_ID,
          projectName: COTTAGE.name,
          name: 'Walls',
          status: 'InProgress',
          dueDate: '2026-09-20',
          isOverdue: true,
          createdAt: '2026-08-10T09:00:00',
        },
        {
          id: 'eeeeeeee-0000-4000-8000-000000000002',
          projectId: COTTAGE_ID,
          projectName: COTTAGE.name,
          name: 'Roof',
          status: 'NotStarted',
          dueDate: '2026-11-03',
          isOverdue: false,
          createdAt: '2026-08-10T09:05:00',
        },
        {
          id: 'eeeeeeee-0000-4000-8000-000000000003',
          projectId: VILLA_ID,
          projectName: VILLA.name,
          name: 'Painting',
          status: 'NotStarted',
          dueDate: null,
          isOverdue: false,
          createdAt: '2026-08-11T09:00:00',
        },
      ],
    },
  }
}

export const PM_PATHS = {
  construction: '/api/construction/dashboard/project-manager',
} as const

export function pmRoutes(
  overrides: Partial<Record<keyof typeof PM_PATHS, Response | Error>> = {},
): Record<string, Response | Error> {
  return {
    [PM_PATHS.construction]: overrides.construction ?? apiResponse(200, pmConstruction()),
  }
}

// ------------------------------------------------------------------ Admin slices ----

/** Ten accounts, nine of which can sign in. */
export function adminUsers(): AdminUsersDashboard {
  return {
    totalUsers: 10,
    activeUsers: 9,
    inactiveUsers: 1,
    roles: [
      { role: 'Client', count: 6 },
      { role: 'Architect', count: 2 },
      { role: 'ProjectManager', count: 1 },
      { role: 'Admin', count: 1 },
    ],
  }
}

/** Sixteen projects, ten of them still active (not completed and not cancelled). */
export function adminProjects(): AdminProjectsDashboard {
  return {
    totalCount: 16,
    groups: [
      { status: 'Pending', count: 3 },
      { status: 'Designing', count: 4 },
      { status: 'DesignApproved', count: 1 },
      { status: 'Construction', count: 2 },
      { status: 'Completed', count: 5 },
      { status: 'Cancelled', count: 1 },
    ],
  }
}

export const ADMIN_PATHS = {
  users: '/api/users/dashboard/admin',
  projects: '/api/projects/dashboard/admin',
} as const

export function adminRoutes(
  overrides: Partial<Record<keyof typeof ADMIN_PATHS, Response | Error>> = {},
): Record<string, Response | Error> {
  return {
    [ADMIN_PATHS.users]: overrides.users ?? apiResponse(200, adminUsers()),
    [ADMIN_PATHS.projects]: overrides.projects ?? apiResponse(200, adminProjects()),
  }
}
