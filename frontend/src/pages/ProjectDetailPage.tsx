import { useCallback, useEffect, useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { Link, useParams } from 'react-router-dom'

import { useAuth } from '@/auth/auth-context'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldError, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { Separator } from '@/components/ui/separator'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { Textarea } from '@/components/ui/textarea'
import { ApiError, apiErrorMessage } from '@/lib/api'
import { fetchAllUsers, type AdminUserSummary } from '@/lib/auth-api'
import {
  createMilestone,
  createMilestonesFromTemplate,
  fetchProjectMilestones,
  fetchProjectProgress,
  MILESTONE_STATUS_LABELS,
  MILESTONE_STATUSES,
  updateMilestoneStatus,
  type Milestone,
  type MilestoneStatus,
  type ProjectProgress,
} from '@/lib/construction-api'
import { createMilestoneSchema, type CreateMilestoneValues } from '@/lib/construction-schemas'
import { applyApiErrorToForm } from '@/lib/form-errors'
import {
  assignArchitect,
  assignProjectManager,
  cancelProject,
  fetchProject,
  fetchProjectEvents,
  updateProjectStatus,
  type ProjectDetail,
  type ProjectEvent,
  type ProjectStatusChange,
} from '@/lib/project-api'
import { deliveryOf, needsAttention } from '@/lib/project-events'
import { PROJECT_STATUS_LABELS, type ProjectStatus } from '@/lib/project-status'
import { ADMIN_ROLES, ROLE_LABELS, STATUS_CHANGE_ROLES } from '@/lib/roles'

/** A date and time the service sent, as a reader would write it. */
function formatMoment(iso: string): string {
  return new Date(iso).toLocaleString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  })
}

/** Grouped digits with the currency named, rather than a bare number. */
function formatMoney(amount: number): string {
  return `LKR ${new Intl.NumberFormat(undefined, { maximumFractionDigits: 2 }).format(amount)}`
}

/**
 * How one history entry reads. The opening entry has no previous status — the
 * project did not come from anywhere, it was created.
 */
function describeChange(change: ProjectStatusChange): string {
  const to = PROJECT_STATUS_LABELS[change.toStatus]

  return change.fromStatus === null
    ? `Created as ${to}`
    : `${PROJECT_STATUS_LABELS[change.fromStatus]} → ${to}`
}

/** Shown when the delivery log itself cannot be read. */
const EVENTS_LOAD_FAILED = 'Could not load this project’s events.'

/** Shown when the list of assignable staff cannot be read. */
const STAFF_LOAD_FAILED = 'Could not load the list of staff to assign.'

/**
 * The message for a failed mutation. A 400 from the service names the field
 * that was wrong — an unknown id, an account that is not the right role, a
 * blank reason — and that specific message beats "one or more validation
 * errors occurred". Anything without a field falls back to the reason the
 * service gave, then the caller's wording.
 */
function describeFailure(error: unknown, fallback: string): string {
  if (error instanceof ApiError) {
    const fieldMessages = Object.values(error.fieldErrors).flat()

    if (fieldMessages.length > 0) {
      return fieldMessages.join(' ')
    }
  }

  return apiErrorMessage(error, fallback)
}

/**
 * One assignment control: a dropdown of the staff who may fill a slot, and a
 * button to commit the choice. Only ever rendered for an Admin.
 */
function AssignRow({
  label,
  placeholder,
  staff,
  value,
  onValueChange,
  onAssign,
  busy,
}: {
  label: string
  placeholder: string
  /** `null` while the list is still loading. */
  staff: AdminUserSummary[] | null
  value: string | null
  onValueChange: (value: string | null) => void
  onAssign: () => void
  busy: boolean
}) {
  const nameFor = (id: string | null) =>
    staff?.find((person) => person.id === id)?.fullName ?? placeholder

  return (
    <div className="flex flex-col gap-2 sm:flex-row sm:items-end">
      <Field className="flex-1">
        <FieldLabel htmlFor={label}>{label}</FieldLabel>
        <Select value={value} onValueChange={onValueChange} disabled={staff === null || busy}>
          <SelectTrigger id={label} className="w-full">
            <SelectValue placeholder={placeholder}>{(id: string | null) => nameFor(id)}</SelectValue>
          </SelectTrigger>
          <SelectContent>
            {(staff ?? []).map((person) => (
              <SelectItem key={person.id} value={person.id}>
                {person.fullName}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </Field>

      {/* aria-label so the two "Assign" buttons on the page are told apart —
          by a screen reader, and by a test. */}
      <Button aria-label={label} onClick={onAssign} disabled={!value || busy}>
        {busy ? 'Assigning…' : 'Assign'}
      </Button>
    </div>
  )
}

/**
 * How one event's delivery reads.
 *
 * A delivered event that took several attempts is shown as delivered, with the
 * count in the small print: that is the outbox having worked, and badging it as
 * a problem would teach people to ignore the badge.
 */
function Delivery({ event }: { event: ProjectEvent }) {
  const delivery = deliveryOf(event)

  if (delivery === 'delivered') {
    return (
      <div className="flex flex-col gap-1">
        <Badge variant="secondary" className="w-fit">
          Delivered
        </Badge>
        <span className="text-muted-foreground text-xs">
          {formatMoment(event.publishedAt as string)}
          {event.attemptCount > 1 && ` · after ${event.attemptCount} attempts`}
        </span>
      </div>
    )
  }

  return (
    <div className="flex flex-col gap-1">
      <Badge variant={delivery === 'retrying' ? 'destructive' : 'outline'} className="w-fit">
        {delivery === 'retrying' ? 'Not delivered' : 'Waiting'}
      </Badge>
      <span className="text-muted-foreground text-xs">
        {delivery === 'retrying'
          ? `${event.attemptCount} ${event.attemptCount === 1 ? 'attempt' : 'attempts'}${
              event.lastError ? ` · ${event.lastError}` : ''
            }`
          : 'Not yet sent'}
      </span>
    </div>
  )
}

/** One labelled fact about the project. */
function Detail({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex flex-col gap-1">
      <span className="text-muted-foreground text-xs uppercase tracking-wide">{label}</span>
      <span className="text-sm">{children}</span>
    </div>
  )
}

/**
 * The construction milestones for a project (US-12) — one row per milestone,
 * with a Select to move each one along and a small form to add the next one.
 * Above the table sits a live progress rollup: a percentage the service
 * recomputes on every read from the milestones behind it, so what shows
 * matches what is stored.
 *
 * Rendered only for a Project Manager: every endpoint behind this section is
 * PM-only, and offering controls the service will refuse is worse than not
 * showing them at all. A caller whose project has not yet had its design
 * approved sees a short explanation of when the section will open rather than
 * a failed request.
 */
function MilestonesSection({
  projectId,
  projectStatus,
}: {
  projectId: string
  projectStatus: ProjectStatus
}) {
  const { authFetch } = useAuth()

  const [milestones, setMilestones] = useState<Milestone[] | null>(null)
  const [progress, setProgress] = useState<ProjectProgress | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [designNotReadyYet, setDesignNotReadyYet] = useState(false)
  const [updatingId, setUpdatingId] = useState<string | null>(null)
  const [updateError, setUpdateError] = useState<string | null>(null)
  const [formError, setFormError] = useState<string | null>(null)
  // "Create from template" one-click flow: applyingTemplate flips true while
  // the POST is in flight so the button can show a busy label and refuse a
  // double-click. templateError surfaces the service's own reason (a design
  // -not-approved 400, or anything else) next to the button.
  const [applyingTemplate, setApplyingTemplate] = useState(false)
  const [templateError, setTemplateError] = useState<string | null>(null)

  const {
    register,
    handleSubmit,
    reset,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<CreateMilestoneValues>({
    resolver: zodResolver(createMilestoneSchema),
    defaultValues: { name: '' },
  })

  // The service refuses milestone reads and writes for a project whose
  // milestone_setups row has not been planted — Pending and Designing are
  // exactly the statuses where that row does not yet exist. Not asking at
  // all beats asking and rendering an error the caller cannot act on.
  // Cancelled is left out on purpose: a closed-out project has no
  // construction plan, so the section would only ever be read-only clutter.
  const canHaveMilestones =
    projectStatus === 'DesignApproved' ||
    projectStatus === 'Construction' ||
    projectStatus === 'Completed'

  /**
   * The load pass — extracted so the initial useEffect and the "Try again"
   * button on the designNotReadyYet variant both run the same code. Wrapped
   * in useCallback so the effect's dependency array stays stable; without
   * that, the effect would refire on every render.
   *
   * Returns a `cancelled` guard the effect can flip during teardown, so a
   * component that unmounts mid-fetch does not call setState on a dead
   * instance. The retry button passes a fresh guard that never flips —
   * the click is a foreground action, not a lifecycle race.
   */
  const load = useCallback(
    async (isCancelled: () => boolean) => {
      // Clear the recoverable states before the new attempt so the UI does
      // not show a stale "try again" hint while the retry is in flight.
      setDesignNotReadyYet(false)
      setLoadError(null)

      try {
        // Both concurrent. Progress can 404 on a narrow race — the project
        // moved to DesignApproved but the DesignApproved event has not been
        // consumed by the Construction Service yet — so a 404 here becomes
        // a short "try again" note rather than a red error.
        const [milestonesResult, progressResult] = await Promise.all([
          fetchProjectMilestones(authFetch, projectId),
          fetchProjectProgress(authFetch, projectId).catch((error: unknown) => {
            if (error instanceof ApiError && error.status === 404) {
              return null
            }
            throw error
          }),
        ])

        if (isCancelled()) {
          return
        }

        setMilestones(milestonesResult)
        setProgress(progressResult)
        setDesignNotReadyYet(progressResult === null)
      } catch (error) {
        if (!isCancelled()) {
          setLoadError(apiErrorMessage(error, 'Could not load milestones for this project.'))
        }
      }
    },
    [authFetch, projectId],
  )

  useEffect(() => {
    if (!canHaveMilestones) {
      return
    }

    let cancelled = false
    void load(() => cancelled)

    return () => {
      cancelled = true
    }
  }, [canHaveMilestones, load])

  /**
   * Re-reads the progress rollup after something has changed the milestones.
   *
   * AC-3: the percentage is recomputed by the service on every read, so this
   * is what makes the number on screen match the row the PM just moved. A
   * 404 that only appears now — after the project used to answer — is the
   * same race as on the initial load; the same friendly note is shown.
   */
  async function refreshProgress() {
    try {
      const fresh = await fetchProjectProgress(authFetch, projectId)
      setProgress(fresh)
      setDesignNotReadyYet(false)
    } catch (error) {
      if (error instanceof ApiError && error.status === 404) {
        setProgress(null)
        setDesignNotReadyYet(true)
      }
      // Any other error: leave the existing progress number in place rather
      // than blank the value the PM is looking at. The status-change error
      // banner already tells them the change itself failed.
    }
  }

  async function onCreate(values: CreateMilestoneValues) {
    setFormError(null)

    try {
      const created = await createMilestone(authFetch, projectId, { name: values.name })

      // Append locally rather than refetch the list — the service ordered the
      // rows oldest-first and this new one is the newest, so it belongs at
      // the end. One fewer request, one less flicker for the PM.
      setMilestones((previous) => (previous ? [...previous, created] : [created]))
      reset({ name: '' })

      await refreshProgress()
    } catch (error) {
      // A 400 detail from the service names the specific reason — the
      // design has not yet been approved, or the name failed validation —
      // and applyApiErrorToForm routes per-field messages to the form.
      // A 409 (duplicate name) has no field to attach to, so its detail
      // reads as the form-level message here.
      setFormError(
        applyApiErrorToForm(error, setError, 'Could not add this milestone. Please try again.'),
      )
    }
  }

  async function onStatusChange(milestone: Milestone, next: MilestoneStatus) {
    if (next === milestone.status) {
      return
    }

    setUpdateError(null)
    setUpdatingId(milestone.id)

    try {
      const updated = await updateMilestoneStatus(authFetch, milestone.id, next)

      setMilestones((previous) =>
        previous ? previous.map((row) => (row.id === updated.id ? updated : row)) : null,
      )

      // AC-3: the percentage recalculates automatically on every status
      // change. The PATCH reply carries the fresh milestone but not the
      // rollup — that is a separate query — so the number below is re-read
      // to keep it in step with the row that just moved.
      await refreshProgress()
    } catch (error) {
      setUpdateError(apiErrorMessage(error, 'Could not update this milestone. Please try again.'))
    } finally {
      setUpdatingId(null)
    }
  }

  /**
   * Plants the seven canonical milestones on the project in one call.
   *
   * The service side is idempotent (names already on the project are
   * silently skipped), so a race with the PM typing a milestone by hand in
   * the same second, or a repeat click, is harmless — the response is the
   * union of what was already there and what was just inserted, which we
   * set as the whole list. The progress rollup then refreshes for AC-3.
   */
  async function applyTemplate() {
    setTemplateError(null)
    setApplyingTemplate(true)

    try {
      const result = await createMilestonesFromTemplate(authFetch, projectId)
      // The response is the full template set (previously-present +
      // newly-inserted) in canonical order — set it as the list rather
      // than appending, since the template call is a state-setting
      // operation from the PM's point of view: "make the project look
      // like the template". Ordering is already correct on the wire.
      setMilestones(result)
      await refreshProgress()
    } catch (error) {
      setTemplateError(
        apiErrorMessage(error, 'Could not apply the template. Please try again.'),
      )
    } finally {
      setApplyingTemplate(false)
    }
  }

  if (!canHaveMilestones) {
    return (
      <section aria-labelledby="milestones-heading" data-testid="milestones-panel" className="flex flex-col gap-3">
        <h2 id="milestones-heading" className="text-sm font-medium">
          Milestones
        </h2>
        <p className="text-muted-foreground text-sm">
          Milestones open once this project's design has been approved.
        </p>
      </section>
    )
  }

  if (designNotReadyYet) {
    return (
      <section aria-labelledby="milestones-heading" data-testid="milestones-panel" className="flex flex-col gap-3">
        <h2 id="milestones-heading" className="text-sm font-medium">
          Milestones
        </h2>
        <p className="text-muted-foreground text-sm">
          This project's design approval hasn't reached the Construction Service yet. Try again in a
          moment.
        </p>
        {/* Recoverable state → give the PM the action they need, so the
            section is not a dead-end that forces a full-page reload. The
            button re-runs the same load pass the effect ran on mount. */}
        <Button
          type="button"
          variant="outline"
          size="sm"
          className="self-start"
          onClick={() => {
            void load(() => false)
          }}
        >
          Try again
        </Button>
      </section>
    )
  }

  if (loadError) {
    return (
      <section aria-labelledby="milestones-heading" data-testid="milestones-panel" className="flex flex-col gap-3">
        <h2 id="milestones-heading" className="text-sm font-medium">
          Milestones
        </h2>
        <p role="alert" className="text-destructive text-sm">
          {loadError}
        </p>
      </section>
    )
  }

  if (milestones === null || progress === null) {
    return (
      <section aria-labelledby="milestones-heading" data-testid="milestones-panel" className="flex flex-col gap-3">
        <h2 id="milestones-heading" className="text-sm font-medium">
          Milestones
        </h2>
        <p className="text-muted-foreground text-sm">Loading milestones…</p>
      </section>
    )
  }

  return (
    <section aria-labelledby="milestones-heading" data-testid="milestones-panel" className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 id="milestones-heading" className="text-sm font-medium">
          Milestones
        </h2>
        <span className="text-muted-foreground text-xs">
          {progress.completedMilestones} of {progress.totalMilestones} completed
        </span>
      </div>

      {/* The rollup: a decimal-precise number and a bar. tabular-nums so the
          two decimals do not shift the layout as the value moves. */}
      <div className="flex flex-col gap-2">
        <div className="flex items-baseline justify-between">
          <span className="text-2xl font-semibold tabular-nums">
            {progress.progressPercent.toFixed(2)}%
          </span>
          <span className="text-muted-foreground text-xs">
            Recalculates automatically as milestones move.
          </span>
        </div>
        <div
          role="progressbar"
          aria-valuenow={Math.round(progress.progressPercent)}
          aria-valuemin={0}
          aria-valuemax={100}
          aria-label="Project progress"
          className="bg-muted h-2 w-full overflow-hidden rounded-full"
        >
          <div
            className="bg-primary h-full transition-[width] duration-500 ease-out"
            style={{ width: `${progress.progressPercent}%` }}
          />
        </div>
      </div>

      {milestones.length === 0 ? (
        // Empty state — the one place the "Create from template" button
        // lives. Once the PM has any milestone (template or hand-typed)
        // the button disappears: the idempotent backend would tolerate a
        // repeat click, but keeping the button visible after the template
        // lands would invite confusion about what a second click would do.
        <div className="flex flex-col gap-3">
          <p className="text-muted-foreground text-sm">
            No milestones defined yet. Add the first one below, or use the standard construction
            template.
          </p>
          <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
            <Button
              type="button"
              variant="secondary"
              size="sm"
              className="self-start"
              onClick={() => {
                void applyTemplate()
              }}
              disabled={applyingTemplate}
            >
              {applyingTemplate ? 'Creating…' : 'Create from template'}
            </Button>
            <span className="text-muted-foreground text-xs">
              Foundation, Walls, Roof, Electrical, Plumbing, Painting, Finishing.
            </span>
          </div>
          {templateError && (
            <p role="alert" className="text-destructive text-sm">
              {templateError}
            </p>
          )}
        </div>
      ) : (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Milestone</TableHead>
              <TableHead>Status</TableHead>
              <TableHead>Last updated</TableHead>
            </TableRow>
          </TableHeader>

          <TableBody>
            {milestones.map((milestone) => (
              <TableRow key={milestone.id}>
                <TableCell className="font-medium">{milestone.name}</TableCell>
                <TableCell>
                  <Select
                    value={milestone.status}
                    onValueChange={(value) => {
                      if (value) {
                        void onStatusChange(milestone, value as MilestoneStatus)
                      }
                    }}
                    disabled={updatingId !== null}
                  >
                    <SelectTrigger
                      className="w-full sm:w-40"
                      aria-label={`Change status of ${milestone.name}`}
                    >
                      <SelectValue>
                        {(value: MilestoneStatus | null) =>
                          value ? MILESTONE_STATUS_LABELS[value] : '—'
                        }
                      </SelectValue>
                    </SelectTrigger>
                    <SelectContent>
                      {MILESTONE_STATUSES.map((status) => (
                        <SelectItem key={status} value={status}>
                          {MILESTONE_STATUS_LABELS[status]}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </TableCell>
                <TableCell className="text-muted-foreground text-xs">
                  {formatMoment(milestone.updatedAtUtc)}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}

      {updateError && (
        <p role="alert" className="text-destructive text-sm">
          {updateError}
        </p>
      )}

      <form
        onSubmit={handleSubmit(onCreate)}
        noValidate
        className="flex flex-col gap-3 sm:flex-row sm:items-end"
      >
        <Field className="flex-1">
          <FieldLabel htmlFor="milestoneName">Add a milestone</FieldLabel>
          <Input
            id="milestoneName"
            placeholder="e.g. Foundation poured"
            aria-invalid={Boolean(errors.name)}
            {...register('name')}
          />
          <FieldError errors={[errors.name]} />
        </Field>
        <Button type="submit" disabled={isSubmitting}>
          {isSubmitting ? 'Adding…' : 'Add'}
        </Button>
      </form>

      {formError && (
        <p role="alert" className="text-destructive text-sm">
          {formError}
        </p>
      )}
    </section>
  )
}

/**
 * One project in full, with its status history, and the controls to move it
 * along (US-06).
 *
 * Open to every role: which projects a caller may actually open is a
 * per-project question the service answers — the owning client, the assigned
 * staff, or an Admin — and a role alone cannot decide it. Somebody who is not
 * on the project gets a 403 with a reason, which is what is shown here.
 */
export function ProjectDetailPage() {
  const { projectId } = useParams<{ projectId: string }>()
  const { authFetch, user } = useAuth()

  const [project, setProject] = useState<ProjectDetail | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [statusError, setStatusError] = useState<string | null>(null)
  const [changingTo, setChangingTo] = useState<ProjectStatus | null>(null)
  const [events, setEvents] = useState<ProjectEvent[] | null>(null)
  const [eventsError, setEventsError] = useState<string | null>(null)

  const [architects, setArchitects] = useState<AdminUserSummary[] | null>(null)
  const [projectManagers, setProjectManagers] = useState<AdminUserSummary[] | null>(null)
  const [architectChoice, setArchitectChoice] = useState<string | null>(null)
  const [pmChoice, setPmChoice] = useState<string | null>(null)
  const [assigning, setAssigning] = useState<'architect' | 'projectManager' | null>(null)
  const [assignError, setAssignError] = useState<string | null>(null)

  const [cancelReason, setCancelReason] = useState('')
  const [cancelling, setCancelling] = useState(false)
  const [cancelError, setCancelError] = useState<string | null>(null)

  // The events view is the integration answering for itself, and the service
  // refuses it to everybody else with a 403. Asking anyway would show every
  // Client an error for something that is not theirs to see. Staff assignment
  // is Admin-only for the same reason.
  const isAdmin = user !== null && ADMIN_ROLES.includes(user.role)

  // The Milestones section (US-12) is Project-Manager only, matching the
  // service's own [Authorize] gate on the endpoints behind it. Offering a
  // control the service will refuse is worse than not showing it at all.
  const isProjectManager = user !== null && user.role === 'ProjectManager'

  useEffect(() => {
    if (!projectId) {
      return
    }

    let cancelled = false

    fetchProject(authFetch, projectId)
      .then((loaded) => {
        if (!cancelled) {
          setProject(loaded)
        }
      })
      .catch((error: unknown) => {
        if (!cancelled) {
          // A 403 or 404 is a real answer with a reason in it — show that
          // reason rather than flattening it into "something went wrong".
          setLoadError(apiErrorMessage(error, 'Could not load this project. Please try again.'))
        }
      })

    return () => {
      cancelled = true
    }
  }, [authFetch, projectId])

  /**
   * Loads the delivery log, separately from the project itself so a failure
   * here cannot take the page down with it — an administrator who cannot reach
   * the events should still see the project.
   */
  useEffect(() => {
    if (!projectId || !isAdmin) {
      return
    }

    let cancelled = false

    fetchProjectEvents(authFetch, projectId)
      .then((loaded) => {
        if (!cancelled) {
          setEvents(loaded)
          setEventsError(null)
        }
      })
      .catch((error: unknown) => {
        if (!cancelled) {
          setEventsError(apiErrorMessage(error, EVENTS_LOAD_FAILED))
        }
      })

    return () => {
      cancelled = true
    }
  }, [authFetch, isAdmin, projectId])

  /**
   * Loads the staff an Admin can assign — active Architects and Project
   * Managers only. The role filter is the service's (`GET /api/users?role=`),
   * so the dropdowns cannot offer somebody the assign endpoint would refuse;
   * deactivated accounts are dropped here.
   */
  useEffect(() => {
    if (!isAdmin) {
      return
    }

    let cancelled = false

    Promise.all([
      fetchAllUsers(authFetch, { role: 'Architect', pageSize: 100 }),
      fetchAllUsers(authFetch, { role: 'ProjectManager', pageSize: 100 }),
    ])
      .then(([architectPage, projectManagerPage]) => {
        if (!cancelled) {
          setArchitects(architectPage.items.filter((person) => person.isActive))
          setProjectManagers(projectManagerPage.items.filter((person) => person.isActive))
        }
      })
      .catch((error: unknown) => {
        if (!cancelled) {
          setAssignError(apiErrorMessage(error, STAFF_LOAD_FAILED))
        }
      })

    return () => {
      cancelled = true
    }
  }, [authFetch, isAdmin])

  /**
   * Re-reads the delivery log after something has changed it.
   *
   * Not cancellation-guarded like the load above, because this only runs from a
   * button the user just pressed on a page that is still mounted.
   */
  async function refreshEvents(id: string) {
    try {
      setEvents(await fetchProjectEvents(authFetch, id))
      setEventsError(null)
    } catch (error) {
      // The move itself succeeded; only the log failed to refresh. Saying so
      // beats leaving a stale list looking current.
      setEventsError(apiErrorMessage(error, EVENTS_LOAD_FAILED))
    }
  }

  /**
   * Moves the project on. The service answers with the project as it now
   * stands, history included, so the page updates from its reply rather than
   * refetching or guessing at the new state.
   */
  async function moveTo(status: ProjectStatus) {
    if (!projectId) {
      return
    }

    setStatusError(null)
    setChangingTo(status)

    try {
      setProject(await updateProjectStatus(authFetch, projectId, status))

      // The move just raised events of its own, so the log below is already out
      // of date. The reply to the PATCH does not carry them — they are written
      // by the same transaction but sent afterwards — so it is re-read.
      if (isAdmin) {
        await refreshEvents(projectId)
      }
    } catch (error) {
      setStatusError(
        apiErrorMessage(error, 'Could not update the status. Please try again.'),
      )
    } finally {
      setChangingTo(null)
    }
  }

  /**
   * Puts the chosen person in a slot. The service answers with the project as
   * it now stands — an Architect assignment on a Pending project comes back
   * Designing, history included — so the page updates from the reply.
   */
  async function assign(slot: 'architect' | 'projectManager') {
    if (!projectId) {
      return
    }

    const staffId = slot === 'architect' ? architectChoice : pmChoice

    if (!staffId) {
      return
    }

    setAssignError(null)
    setAssigning(slot)

    try {
      const updated =
        slot === 'architect'
          ? await assignArchitect(authFetch, projectId, staffId)
          : await assignProjectManager(authFetch, projectId, staffId)

      setProject(updated)

      if (slot === 'architect') {
        setArchitectChoice(null)
        // Assigning an Architect to a Pending project moves it to Designing,
        // which raises an event — so the log below is out of date.
        if (isAdmin) {
          await refreshEvents(projectId)
        }
      } else {
        setPmChoice(null)
      }
    } catch (error) {
      setAssignError(
        describeFailure(error, 'Could not assign this person. Please try again.'),
      )
    } finally {
      setAssigning(null)
    }
  }

  /**
   * Closes the project out (US-08). Terminal — the service answers with the
   * project as it now stands, Cancelled, its history carrying the reason — so
   * the page updates from the reply.
   */
  async function cancel() {
    if (!projectId || cancelReason.trim().length === 0) {
      return
    }

    setCancelError(null)
    setCancelling(true)

    try {
      setProject(await cancelProject(authFetch, projectId, cancelReason.trim()))
      setCancelReason('')

      // Cancellation raises a ProjectUpdated, so the log below is out of date.
      if (isAdmin) {
        await refreshEvents(projectId)
      }
    } catch (error) {
      setCancelError(describeFailure(error, 'Could not cancel this project. Please try again.'))
    } finally {
      setCancelling(false)
    }
  }

  if (loadError) {
    return (
      <main className="mx-auto flex min-h-svh w-full max-w-2xl flex-col justify-center gap-4 p-6">
        <Card>
          <CardHeader>
            <CardTitle>This project is not available to you</CardTitle>
            <CardDescription role="alert">{loadError}</CardDescription>
          </CardHeader>

          <CardContent>
            <Button render={<Link to="/projects" />} variant="outline" className="w-full">
              Back to projects
            </Button>
          </CardContent>
        </Card>
      </main>
    )
  }

  if (!project) {
    return (
      <main className="mx-auto flex min-h-svh w-full max-w-2xl flex-col justify-center gap-4 p-6">
        <p className="text-muted-foreground text-sm">Loading this project…</p>
      </main>
    )
  }

  // The service decides what the project may move to; this only decides whether
  // to offer it. Staff who are not on this project still get a 403 when they
  // try, which is the answer that counts.
  const canChangeStatus = user !== null && STATUS_CHANGE_ROLES.includes(user.role)
  const nextStatuses = canChangeStatus ? project.allowedNextStatuses : []

  // US-08: the owning Client or an Admin, and only before construction starts.
  // The service enforces both — this just decides whether to show the control.
  const isOwningClient = user !== null && user.role === 'Client' && project.clientId === user.id
  const canCancel =
    (isAdmin || isOwningClient) &&
    (project.status === 'Pending' ||
      project.status === 'Designing' ||
      project.status === 'DesignApproved')

  return (
    <main className="mx-auto flex min-h-svh w-full max-w-3xl flex-col justify-center gap-4 p-6">
      <Card>
        <CardHeader>
          <CardTitle className="flex flex-wrap items-center gap-3">
            {project.name}
            <Badge variant="secondary">{PROJECT_STATUS_LABELS[project.status]}</Badge>
          </CardTitle>
          <CardDescription>
            {project.location} · submitted {formatDate(project.createdAt)}
          </CardDescription>
        </CardHeader>

        <CardContent className="flex flex-col gap-6">
          <section className="flex flex-col gap-3">
            <h2 className="text-sm font-medium">Requirements</h2>

            <div className="grid gap-4 sm:grid-cols-3">
              <Detail label="Land size">{project.landSizePerches} perches</Detail>
              <Detail label="Budget">{formatMoney(project.budget)}</Detail>
              <Detail label="Floors">{project.floors}</Detail>
              <Detail label="Bedrooms">{project.bedrooms}</Detail>
              <Detail label="Bathrooms">{project.bathrooms}</Detail>
              <Detail label="Garage spaces">{project.garageSpaces}</Detail>
            </div>

            <Detail label="Other requirements">
              {project.otherRequirements ?? (
                <span className="text-muted-foreground">Nothing further was asked for.</span>
              )}
            </Detail>
          </section>

          <Separator />

          <section className="flex flex-col gap-3">
            <h2 className="text-sm font-medium">Team</h2>

            {/* Ids rather than names: the accounts live in the User Service's
                own database, and the Project Service holds no copy of them. */}
            <div className="grid gap-4 sm:grid-cols-2">
              <Detail label="Architect">
                {project.assignedArchitectId ?? (
                  <span className="text-muted-foreground">Not yet assigned</span>
                )}
              </Detail>
              <Detail label="Project manager">
                {project.assignedProjectManagerId ?? (
                  <span className="text-muted-foreground">Not yet assigned</span>
                )}
              </Detail>
            </div>

            {isAdmin && (
              <div className="flex flex-col gap-4">
                <AssignRow
                  label="Assign architect"
                  placeholder={architects === null ? 'Loading…' : 'Choose an architect'}
                  staff={architects}
                  value={architectChoice}
                  onValueChange={setArchitectChoice}
                  onAssign={() => assign('architect')}
                  busy={assigning === 'architect'}
                />
                <AssignRow
                  label="Assign project manager"
                  placeholder={projectManagers === null ? 'Loading…' : 'Choose a project manager'}
                  staff={projectManagers}
                  value={pmChoice}
                  onValueChange={setPmChoice}
                  onAssign={() => assign('projectManager')}
                  busy={assigning === 'projectManager'}
                />

                <p className="text-muted-foreground text-sm">
                  Assigning an architect while this project is still Pending moves it to Designing.
                </p>

                {assignError && (
                  <p role="alert" className="text-destructive text-sm">
                    {assignError}
                  </p>
                )}
              </div>
            )}
          </section>

          <Separator />

          <section className="flex flex-col gap-3">
            <h2 className="text-sm font-medium">Status history</h2>

            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Change</TableHead>
                  <TableHead>By</TableHead>
                  <TableHead>When</TableHead>
                </TableRow>
              </TableHeader>

              <TableBody>
                {/* Oldest first, exactly as the service sent it — nothing here
                    re-sorts, so the page cannot disagree with the record. */}
                {project.statusHistory.map((change) => (
                  <TableRow key={change.id}>
                    <TableCell className="font-medium">
                      {describeChange(change)}
                      {change.note && (
                        <span className="text-muted-foreground block text-xs font-normal">
                          {change.note}
                        </span>
                      )}
                    </TableCell>
                    <TableCell>
                      <span className="block">{ROLE_LABELS[change.changedByRole]}</span>
                      <span className="text-muted-foreground block text-xs">
                        {change.changedByUserId}
                      </span>
                    </TableCell>
                    <TableCell>{formatMoment(change.changedAt)}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </section>

          {canChangeStatus && (
            <>
              <Separator />

              <section className="flex flex-col gap-3">
                <h2 className="text-sm font-medium">Move this project on</h2>

                {nextStatuses.length === 0 ? (
                  <p className="text-muted-foreground text-sm">
                    This project is {PROJECT_STATUS_LABELS[project.status]} and cannot move any
                    further.
                  </p>
                ) : (
                  <div className="flex flex-wrap gap-3">
                    {nextStatuses.map((status) => (
                      <Button
                        key={status}
                        onClick={() => moveTo(status)}
                        disabled={changingTo !== null}
                      >
                        {changingTo === status
                          ? 'Updating…'
                          : `Move to ${PROJECT_STATUS_LABELS[status]}`}
                      </Button>
                    ))}
                  </div>
                )}

                {statusError && (
                  <p role="alert" className="text-destructive text-sm">
                    {statusError}
                  </p>
                )}
              </section>
            </>
          )}

          {canCancel && (
            <>
              <Separator />

              <section className="flex flex-col gap-3">
                <h2 className="text-sm font-medium">Cancel this project</h2>

                <p className="text-muted-foreground text-sm">
                  Closes the project out before construction starts. It cannot be undone — the
                  project stays viewable, but it drops off the active project list.
                </p>

                <Field>
                  <FieldLabel htmlFor="cancelReason">Reason</FieldLabel>
                  <Textarea
                    id="cancelReason"
                    rows={2}
                    value={cancelReason}
                    onChange={(event) => setCancelReason(event.target.value)}
                  />
                </Field>

                <Button
                  variant="destructive"
                  className="w-fit"
                  onClick={cancel}
                  disabled={cancelReason.trim().length === 0 || cancelling}
                >
                  {cancelling ? 'Cancelling…' : 'Cancel project'}
                </Button>

                {cancelError && (
                  <p role="alert" className="text-destructive text-sm">
                    {cancelError}
                  </p>
                )}
              </section>
            </>
          )}

          {isAdmin && (
            <>
              <Separator />

              <section className="flex flex-col gap-3">
                <div className="flex flex-wrap items-center gap-3">
                  <h2 className="text-sm font-medium">Integration events</h2>
                  {events !== null && needsAttention(events) && (
                    <Badge variant="destructive">Needs attention</Badge>
                  )}
                </div>

                <p className="text-muted-foreground text-sm">
                  What this project announced to the other services. An event is recorded in the
                  same transaction as the change it describes and sent afterwards, so one that has
                  not gone yet is delayed rather than lost.
                </p>

                {eventsError ? (
                  <p role="alert" className="text-destructive text-sm">
                    {eventsError}
                  </p>
                ) : events === null ? (
                  <p className="text-muted-foreground text-sm">Loading events…</p>
                ) : events.length === 0 ? (
                  <p className="text-muted-foreground text-sm">
                    This project has not announced anything yet.
                  </p>
                ) : (
                  <Table>
                    <TableHeader>
                      <TableRow>
                        <TableHead>Event</TableHead>
                        <TableHead>Raised</TableHead>
                        <TableHead>Delivery</TableHead>
                      </TableRow>
                    </TableHeader>

                    <TableBody>
                      {/* Oldest first, exactly as the service sent it — which is
                          also the order the events go onto the topic. */}
                      {events.map((event) => (
                        <TableRow key={event.id}>
                          <TableCell>
                            {/* The wire value, not a friendly name: it is what a
                                consumer subscribes to, so it is what an
                                administrator needs to match against. */}
                            <span className="font-mono text-xs font-medium">{event.eventType}</span>
                            <span className="text-muted-foreground block text-xs">{event.id}</span>
                          </TableCell>
                          <TableCell>{formatMoment(event.occurredAt)}</TableCell>
                          <TableCell>
                            <Delivery event={event} />
                          </TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                )}
              </section>
            </>
          )}

          {isProjectManager && (
            <>
              <Separator />
              <MilestonesSection projectId={project.id} projectStatus={project.status} />
            </>
          )}

          <Button render={<Link to={`/projects/${project.id}/designs`} />} variant="outline" className="w-full">
            Design documents
          </Button>

          <p className="text-muted-foreground text-center text-sm">
            <Link to="/projects" className="text-foreground underline underline-offset-4">
              Back to projects
            </Link>
          </p>
        </CardContent>
      </Card>
    </main>
  )
}