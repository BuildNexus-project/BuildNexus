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
import { ApiError } from '@/lib/api'
import { fetchProjectStaffDirectory, type DirectoryEntry } from '@/lib/auth-api'
import { ROLE_LABELS } from '@/lib/roles'

/**
 * The Architects and Project Managers available to work on a project (US-03).
 *
 * Open to those same two roles. A Client is kept out — a customer has no
 * business browsing the firm's staff — and so is an Admin, who administers
 * accounts through the user directory rather than taking part in project work.
 */
export function StaffDirectoryPage() {
  const { authFetch } = useAuth()

  const [staff, setStaff] = useState<DirectoryEntry[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false

    fetchProjectStaffDirectory(authFetch)
      .then((loaded) => {
        if (!cancelled) {
          setStaff(loaded)
        }
      })
      .catch((error: unknown) => {
        if (cancelled) {
          return
        }

        // A 403 is a real answer with a reason in it — show that reason rather
        // than flattening it into "something went wrong".
        setLoadError(
          error instanceof ApiError && error.status === 403
            ? (error.detail ?? 'Your role does not permit this action.')
            : 'Could not load the project team. Please try again.',
        )
      })

    // The effect can outlive the page if the user navigates away mid-request.
    return () => {
      cancelled = true
    }
  }, [authFetch])

  return (
    <main className="mx-auto flex min-h-svh w-full max-w-2xl flex-col justify-center gap-4 p-6">
      <Card>
        <CardHeader>
          <CardTitle>Project team</CardTitle>
          <CardDescription>
            The Architects and Project Managers available to work on a project. Deactivated
            accounts are left out — they cannot be given work.
          </CardDescription>
        </CardHeader>

        <CardContent className="flex flex-col gap-4">
          {loadError && (
            <p role="alert" className="text-destructive text-sm">
              {loadError}
            </p>
          )}

          {!loadError && !staff && (
            <p className="text-muted-foreground text-sm">Loading the project team…</p>
          )}

          {staff && staff.length === 0 && (
            <p className="text-muted-foreground text-sm">
              No Architects or Project Managers have active accounts yet.
            </p>
          )}

          {staff && staff.length > 0 && (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Name</TableHead>
                  <TableHead>Role</TableHead>
                </TableRow>
              </TableHeader>

              <TableBody>
                {staff.map((member) => (
                  <TableRow key={member.id}>
                    <TableCell className="font-medium">{member.fullName}</TableCell>
                    <TableCell>
                      <Badge variant="secondary">{ROLE_LABELS[member.role]}</Badge>
                    </TableCell>
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
