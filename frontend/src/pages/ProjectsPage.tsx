import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'

import { useAuth } from '@/auth/auth-context'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { apiErrorMessage } from '@/lib/api'
import { fetchProjects, type ProjectSummary } from '@/lib/project-api'
import { PROJECT_STATUS_LABELS } from '@/lib/project-status'
import { CLIENT_ROLES } from '@/lib/roles'

/** A date the service sent, as a reader would write it. */
function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  })
}

/**
 * The projects the signed-in user is party to (US-06) — the way into a single
 * project's details and status history.
 *
 * Open to every role, because the role is not what decides what is in the list:
 * the service scopes it to the caller's own involvement, so a Client sees the
 * projects they submitted, staff the ones they are assigned to, and an Admin
 * all of them. Nobody is shown a project they would be refused when they
 * clicked it.
 */
export function ProjectsPage() {
  const { authFetch, user } = useAuth()

  const [projects, setProjects] = useState<ProjectSummary[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false

    fetchProjects(authFetch)
      .then((loaded) => {
        if (!cancelled) {
          setProjects(loaded)
        }
      })
      .catch((error: unknown) => {
        if (!cancelled) {
          setLoadError(apiErrorMessage(error, 'Could not load your projects. Please try again.'))
        }
      })

    // The effect can outlive the page if the user navigates away mid-request.
    return () => {
      cancelled = true
    }
  }, [authFetch])

  const isClient = user !== null && CLIENT_ROLES.includes(user.role)

  return (
    <main className="mx-auto flex min-h-svh w-full max-w-3xl flex-col justify-center gap-4 p-6">
      <Card>
        <CardHeader>
          <CardTitle>Projects</CardTitle>
          <CardDescription>
            {isClient
              ? 'The projects you have submitted. Open one to see where it stands.'
              : 'The projects you are working on. Open one to see its details and history.'}
          </CardDescription>
        </CardHeader>

        <CardContent className="flex flex-col gap-4">
          {loadError && (
            <p role="alert" className="text-destructive text-sm">
              {loadError}
            </p>
          )}

          {!loadError && !projects && (
            <p className="text-muted-foreground text-sm">Loading your projects…</p>
          )}

          {projects && projects.length === 0 && (
            <p className="text-muted-foreground text-sm">
              {isClient
                ? 'You have not submitted a project yet.'
                : 'You have not been assigned to a project yet.'}
            </p>
          )}

          {projects && projects.length > 0 && (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Project</TableHead>
                  <TableHead>Location</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead>Submitted</TableHead>
                </TableRow>
              </TableHeader>

              <TableBody>
                {projects.map((project) => (
                  <TableRow key={project.id}>
                    <TableCell className="font-medium">
                      <Link
                        to={`/projects/${project.id}`}
                        className="underline underline-offset-4"
                      >
                        {project.name}
                      </Link>
                    </TableCell>
                    <TableCell>{project.location}</TableCell>
                    <TableCell>
                      <Badge variant="secondary">{PROJECT_STATUS_LABELS[project.status]}</Badge>
                    </TableCell>
                    <TableCell>{formatDate(project.createdAt)}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}

          <p className="text-muted-foreground text-center text-sm">
            <Link to="/" className="text-foreground underline underline-offset-4">
              Back to home
            </Link>
          </p>
        </CardContent>
      </Card>
    </main>
  )
}
