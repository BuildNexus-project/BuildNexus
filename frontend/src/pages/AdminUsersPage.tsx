import { useEffect, useState } from 'react'
import { Controller, useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { Link } from 'react-router-dom'

import { useAuth } from '@/auth/auth-context'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Field, FieldDescription, FieldError, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { ApiError, apiErrorMessage } from '@/lib/api'
import {
  fetchAllUsers,
  setUserActive,
  updateUser,
  type AdminUserSummary,
  type Page,
} from '@/lib/auth-api'
import { adminUserSchema, type AdminUserValues } from '@/lib/auth-schemas'
import { applyApiErrorToForm } from '@/lib/form-errors'
import { ROLES, ROLE_LABELS, type Role } from '@/lib/roles'

/** How many accounts a page shows. Within the 1–100 the service will build. */
const PAGE_SIZE = 10

/**
 * The role filter's "no filter" option.
 *
 * A sentinel rather than an empty string: the select needs a value for every
 * item, and `''` is what the trigger shows when nothing is chosen at all.
 */
const ALL_ROLES = 'all'

type RoleFilter = Role | typeof ALL_ROLES

export function AdminUsersPage() {
  const { authFetch, user: signedInUser } = useAuth()

  const [directory, setDirectory] = useState<Page<AdminUserSummary> | null>(null)
  const [page, setPage] = useState(1)
  const [roleFilter, setRoleFilter] = useState<RoleFilter>(ALL_ROLES)
  const [loadError, setLoadError] = useState<string | null>(null)

  /** The account being edited, or `null` when the dialog is closed. */
  const [editing, setEditing] = useState<AdminUserSummary | null>(null)
  /** The account being deactivated, held until the confirmation is answered. */
  const [deactivating, setDeactivating] = useState<AdminUserSummary | null>(null)
  /** Whichever row has a request in flight, so only its own button goes quiet. */
  const [busyUserId, setBusyUserId] = useState<string | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)

  // Re-runs whenever the page or the filter moves, which is what makes both of
  // them work: the service does the paging and the filtering, so asking again
  // is the whole of it.
  useEffect(() => {
    let cancelled = false

    fetchAllUsers(authFetch, {
      role: roleFilter === ALL_ROLES ? undefined : roleFilter,
      page,
      pageSize: PAGE_SIZE,
    })
      .then((loaded) => {
        if (!cancelled) {
          setDirectory(loaded)
          // Cleared here rather than before the request, so a load that
          // succeeds after one that failed puts the table back on its own.
          setLoadError(null)
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

    // The effect can outlive the page if the user navigates away mid-request,
    // and a filter changed twice quickly can land its answers out of order.
    return () => {
      cancelled = true
    }
  }, [authFetch, page, roleFilter])

  /**
   * Puts a changed account back in the page in place.
   *
   * The row the service returns, not the one submitted: that is what was
   * stored. Re-reading the whole page would be the alternative, and it would
   * move rows around under the administrator when a rename changes the
   * ordering.
   */
  function replaceRow(saved: AdminUserSummary) {
    setDirectory((current) =>
      current === null
        ? current
        : { ...current, items: current.items.map((row) => (row.id === saved.id ? saved : row)) },
    )
  }

  async function onSetActive(target: AdminUserSummary, isActive: boolean) {
    setActionError(null)
    setBusyUserId(target.id)

    try {
      replaceRow(await setUserActive(authFetch, target.id, isActive))
      setDeactivating(null)
    } catch (error) {
      setActionError(
        apiErrorMessage(
          error,
          `Could not ${isActive ? 'reinstate' : 'deactivate'} this account. Please try again.`,
        ),
      )
    } finally {
      setBusyUserId(null)
    }
  }

  const totalPages = directory?.totalPages ?? 1
  const isEmpty = directory !== null && directory.items.length === 0

  return (
    <main className="mx-auto flex min-h-svh w-full max-w-5xl flex-col justify-center gap-4 p-6">
      <Card>
        <CardHeader>
          <CardTitle>User directory</CardTitle>
          <CardDescription>
            Every account on the platform, including those that have been deactivated. Edit a
            person's details or withdraw their access. Visible to administrators only.
          </CardDescription>
        </CardHeader>

        <CardContent className="flex flex-col gap-4">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <Field className="w-full max-w-56">
              <FieldLabel htmlFor="roleFilter">Filter by role</FieldLabel>
              <Select
                value={roleFilter}
                onValueChange={(value) => {
                  setRoleFilter(value as RoleFilter)
                  // A narrower list has fewer pages; staying on page 7 of the
                  // old one would show an empty table.
                  setPage(1)
                }}
              >
                <SelectTrigger id="roleFilter" className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={ALL_ROLES}>All roles</SelectItem>
                  {ROLES.map((role) => (
                    <SelectItem key={role} value={role}>
                      {ROLE_LABELS[role]}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </Field>

            {directory && (
              <p className="text-muted-foreground text-sm">
                {directory.totalCount} {directory.totalCount === 1 ? 'account' : 'accounts'}
              </p>
            )}
          </div>

          {loadError && (
            <p role="alert" className="text-destructive text-sm">
              {loadError}
            </p>
          )}

          {actionError && (
            <p role="alert" className="text-destructive text-sm">
              {actionError}
            </p>
          )}

          {!loadError && !directory && (
            <p className="text-muted-foreground text-sm">Loading the directory…</p>
          )}

          {isEmpty && (
            <p className="text-muted-foreground text-sm">
              {roleFilter === ALL_ROLES
                ? 'There are no accounts to show.'
                : `No account holds the ${ROLE_LABELS[roleFilter]} role.`}
            </p>
          )}

          {directory && directory.items.length > 0 && (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Name</TableHead>
                  <TableHead>Email</TableHead>
                  <TableHead>Role</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead>Registered</TableHead>
                  <TableHead className="text-right">Actions</TableHead>
                </TableRow>
              </TableHeader>

              <TableBody>
                {directory.items.map((row) => {
                  // The service refuses an administrator deactivating their own
                  // account — that refusal is what keeps an active administrator
                  // on the platform — so the button is not offered either.
                  const isSelf = row.id === signedInUser?.id
                  const isBusy = busyUserId === row.id

                  return (
                    <TableRow key={row.id}>
                      <TableCell className="font-medium">{row.fullName}</TableCell>
                      <TableCell className="text-muted-foreground">{row.email}</TableCell>
                      <TableCell>
                        <Badge variant="secondary">{ROLE_LABELS[row.role]}</Badge>
                      </TableCell>
                      <TableCell>
                        {row.isActive ? (
                          <Badge variant="outline">Active</Badge>
                        ) : (
                          <Badge variant="destructive">Deactivated</Badge>
                        )}
                      </TableCell>
                      <TableCell className="text-muted-foreground">
                        {new Date(row.createdAt).toLocaleDateString()}
                      </TableCell>
                      <TableCell>
                        <div className="flex justify-end gap-2">
                          <Button
                            size="sm"
                            variant="outline"
                            disabled={isBusy}
                            onClick={() => {
                              setActionError(null)
                              setEditing(row)
                            }}
                          >
                            Edit
                          </Button>

                          {row.isActive ? (
                            <Button
                              size="sm"
                              variant="destructive"
                              disabled={isSelf || isBusy}
                              title={
                                isSelf
                                  ? 'You cannot deactivate your own account.'
                                  : `Deactivate ${row.fullName}`
                              }
                              onClick={() => {
                                setActionError(null)
                                setDeactivating(row)
                              }}
                            >
                              Deactivate
                            </Button>
                          ) : (
                            <Button
                              size="sm"
                              variant="outline"
                              disabled={isBusy}
                              onClick={() => void onSetActive(row, true)}
                            >
                              {isBusy ? 'Working…' : 'Reinstate'}
                            </Button>
                          )}
                        </div>
                      </TableCell>
                    </TableRow>
                  )
                })}
              </TableBody>
            </Table>
          )}

          {directory && directory.items.length > 0 && (
            <div className="flex items-center justify-between gap-3">
              <Button
                size="sm"
                variant="outline"
                disabled={page <= 1}
                onClick={() => setPage((current) => current - 1)}
              >
                Previous
              </Button>

              <p className="text-muted-foreground text-sm">
                Page {directory.page} of {totalPages}
              </p>

              <Button
                size="sm"
                variant="outline"
                disabled={page >= totalPages}
                onClick={() => setPage((current) => current + 1)}
              >
                Next
              </Button>
            </div>
          )}

          <p className="text-muted-foreground text-center text-sm">
            <Link to="/" className="text-foreground underline underline-offset-4">
              Back to home
            </Link>
          </p>
        </CardContent>
      </Card>

      {editing && (
        <EditUserDialog
          account={editing}
          // An administrator cannot change their own role; the service refuses
          // it, so the control is locked rather than offering a doomed edit.
          isSelf={editing.id === signedInUser?.id}
          onClose={() => setEditing(null)}
          onSaved={(saved) => {
            replaceRow(saved)
            setEditing(null)
          }}
        />
      )}

      <Dialog
        open={deactivating !== null}
        onOpenChange={(open) => {
          if (!open) {
            setDeactivating(null)
          }
        }}
      >
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Deactivate this account?</DialogTitle>
            <DialogDescription>
              {deactivating?.fullName} will no longer be able to sign in, and a password reset will
              not let them back in. Everything they have already submitted stays as it is, and you
              can reinstate the account at any time.
            </DialogDescription>
          </DialogHeader>

          <DialogFooter>
            <Button variant="outline" onClick={() => setDeactivating(null)}>
              Cancel
            </Button>
            <Button
              variant="destructive"
              disabled={busyUserId !== null}
              onClick={() => {
                if (deactivating) {
                  void onSetActive(deactivating, false)
                }
              }}
            >
              {busyUserId !== null ? 'Deactivating…' : 'Deactivate'}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </main>
  )
}

/**
 * Edits one account's name, email and role.
 *
 * Mounted only while an account is selected, so the form starts from that row
 * rather than needing to be reset when the selection changes.
 */
function EditUserDialog({
  account,
  isSelf,
  onClose,
  onSaved,
}: {
  account: AdminUserSummary
  isSelf: boolean
  onClose: () => void
  onSaved: (saved: AdminUserSummary) => void
}) {
  const { authFetch } = useAuth()
  const [formError, setFormError] = useState<string | null>(null)

  const {
    register,
    handleSubmit,
    control,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<AdminUserValues>({
    resolver: zodResolver(adminUserSchema),
    defaultValues: {
      fullName: account.fullName,
      email: account.email,
      role: account.role,
    },
  })

  async function onSubmit(values: AdminUserValues) {
    setFormError(null)

    try {
      onSaved(await updateUser(authFetch, account.id, values))
    } catch (error) {
      // A 409 on the email and the service's own refusals both arrive with
      // something specific to say; only an unrecognised failure falls back.
      setFormError(
        applyApiErrorToForm(error, setError, 'Could not save these changes. Please try again.'),
      )
    }
  }

  return (
    <Dialog
      open
      onOpenChange={(open) => {
        if (!open) {
          onClose()
        }
      }}
    >
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Edit account</DialogTitle>
          <DialogDescription>
            Change this person's name, the email they sign in with, or what they may do on the
            platform.
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={handleSubmit(onSubmit)} noValidate className="flex flex-col gap-5">
          <Field>
            <FieldLabel htmlFor="fullName">Full name</FieldLabel>
            <Input
              id="fullName"
              autoComplete="name"
              aria-invalid={Boolean(errors.fullName)}
              {...register('fullName')}
            />
            <FieldError errors={[errors.fullName]} />
          </Field>

          <Field>
            <FieldLabel htmlFor="email">Email</FieldLabel>
            <Input
              id="email"
              type="email"
              autoComplete="email"
              aria-invalid={Boolean(errors.email)}
              {...register('email')}
            />
            <FieldDescription>
              This is the address they sign in with. Changing it changes their login.
            </FieldDescription>
            <FieldError errors={[errors.email]} />
          </Field>

          <Field>
            <FieldLabel htmlFor="role">Role</FieldLabel>
            <Controller
              control={control}
              name="role"
              render={({ field }) => (
                <Select value={field.value ?? null} onValueChange={field.onChange} disabled={isSelf}>
                  <SelectTrigger id="role" aria-invalid={Boolean(errors.role)} className="w-full">
                    <SelectValue placeholder="Select a role" />
                  </SelectTrigger>
                  <SelectContent>
                    {ROLES.map((role) => (
                      <SelectItem key={role} value={role}>
                        {ROLE_LABELS[role]}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            />
            {isSelf && (
              <FieldDescription>
                You cannot change your own role. Ask another administrator to do it.
              </FieldDescription>
            )}
            <FieldError errors={[errors.role]} />
          </Field>

          {formError && (
            <p role="alert" className="text-destructive text-sm">
              {formError}
            </p>
          )}

          <DialogFooter>
            <Button type="button" variant="outline" onClick={onClose} disabled={isSubmitting}>
              Cancel
            </Button>
            <Button type="submit" disabled={isSubmitting}>
              {isSubmitting ? 'Saving…' : 'Save changes'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
