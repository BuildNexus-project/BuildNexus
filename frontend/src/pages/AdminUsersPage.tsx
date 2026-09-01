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
import { fetchAllUsers, type AdminUserSummary } from '@/lib/auth-api'
import { ROLE_LABELS } from '@/lib/roles'

/**
 * Every account on the platform — Admin only (US-03).
 *
 * The route guard keeps other roles out, but this page does not rely on it: the
 * service answers 403 to anyone else, and that answer is shown as it comes.
 */
export function AdminUsersPage() {
  const { authFetch } = useAuth()

  const [users, setUsers] = useState<AdminUserSummary[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false

    fetchAllUsers(authFetch)
      .then((loaded) => {
        if (!cancelled) {
          // The first page only, until the paging and filtering controls land.
          setUsers(loaded.items)
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
            : 'Could not load the user directory. Please try again.',
        )
      })

    // The effect can outlive the page if the user navigates away mid-request.
    return () => {
      cancelled = true
    }
  }, [authFetch])

  return (
    <main className="mx-auto flex min-h-svh w-full max-w-4xl flex-col justify-center gap-4 p-6">
      <Card>
        <CardHeader>
          <CardTitle>User directory</CardTitle>
          <CardDescription>
            Every account on the platform, including those that have been deactivated. Visible to
            administrators only.
          </CardDescription>
        </CardHeader>

        <CardContent className="flex flex-col gap-4">
          {loadError && (
            <p role="alert" className="text-destructive text-sm">
              {loadError}
            </p>
          )}

          {!loadError && !users && (
            <p className="text-muted-foreground text-sm">Loading the directory…</p>
          )}

          {users && (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Name</TableHead>
                  <TableHead>Email</TableHead>
                  <TableHead>Role</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead>Registered</TableHead>
                </TableRow>
              </TableHeader>

              <TableBody>
                {users.map((user) => (
                  <TableRow key={user.id}>
                    <TableCell className="font-medium">{user.fullName}</TableCell>
                    <TableCell className="text-muted-foreground">{user.email}</TableCell>
                    <TableCell>
                      <Badge variant="secondary">{ROLE_LABELS[user.role]}</Badge>
                    </TableCell>
                    <TableCell>
                      {user.isActive ? (
                        <Badge variant="outline">Active</Badge>
                      ) : (
                        <Badge variant="destructive">Deactivated</Badge>
                      )}
                    </TableCell>
                    <TableCell className="text-muted-foreground">
                      {new Date(user.createdAt).toLocaleDateString()}
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
