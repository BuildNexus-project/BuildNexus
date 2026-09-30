import { zodResolver } from '@hookform/resolvers/zod'
import { useEffect, useState } from 'react'
import { Controller, useForm } from 'react-hook-form'
import { Link } from 'react-router-dom'

import { useAuth } from '@/auth/auth-context'
import { StatusBadge } from '@/components/StatusBadge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import {
  Field,
  FieldError,
  FieldLabel,
  FieldLegend,
  FieldSet,
} from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { apiErrorMessage } from '@/lib/api'
import {
  downloadProjectStatusReportCsv,
  fetchProjectStatusReport,
  projectReportFileName,
  type ProjectStatusReport,
} from '@/lib/project-report-api'
import {
  EMPTY_PROJECT_REPORT_FILTER,
  projectReportFilterSchema,
  type ProjectReportFilterValues,
} from '@/lib/project-report-schemas'
import { PROJECT_STATUSES, PROJECT_STATUS_LABELS, type ProjectStatus } from '@/lib/project-status'

const LOAD_FAILED = 'Could not load the project report.'
const EXPORT_FAILED = 'Could not export the report. Please try again.'

/**
 * How many projects a group lists before it offers "Show all". The service
 * returns every matching project — the export needs them all — but a group of
 * several hundred rows drowns the rest of the pipeline, and the count and budget
 * beside each status already say how big it is.
 */
export const GROUP_PREVIEW_LIMIT = 50

/** Money as a reader would write it — grouped, two decimals, no currency symbol invented. */
function formatAmount(value: number): string {
  return value.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })
}

/**
 * The day a project was submitted, as the `yyyy-MM-dd` the date filter speaks.
 *
 * Read off the timestamp rather than run through the browser's timezone: the
 * report's date range is in UTC days, so the day shown beside a project is the
 * day the filter would match it on. A Colombo user filtering on 1 September
 * should not see that project dated 31 August, or 2 September.
 */
function submittedDay(iso: string): string {
  return iso.slice(0, 10)
}

/** What the last request for a given filter came back with. */
type Loaded = { filter: ProjectReportFilterValues } & (
  | { report: ProjectStatusReport }
  | { error: string }
)

/** The Blob handed to the browser as a file to save — there is no other way to give it bytes it already fetched. */
function saveBlob(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  document.body.appendChild(link)
  link.click()
  link.remove()
  URL.revokeObjectURL(url)
}

/**
 * The project status report (US-18): every project grouped by its current
 * status, so the whole pipeline can be read at a glance, with a status filter,
 * a date range over the day projects were submitted, and a CSV export of
 * whatever is on screen.
 *
 * Admin only — the route guards it, and the endpoint behind it answers 403 to
 * anyone else.
 */
export function ProjectStatusReportPage() {
  const { authFetch, token } = useAuth()

  // What the form holds is a draft; `applied` is what the report on screen was
  // asked for. Kept apart so ticking a box does not fire a request, and so the
  // export is always of the report the Admin is looking at rather than of a
  // filter they have only half changed.
  const [applied, setApplied] = useState<ProjectReportFilterValues>(EMPTY_PROJECT_REPORT_FILTER)
  const [loaded, setLoaded] = useState<Loaded | null>(null)
  const [expanded, setExpanded] = useState<ProjectStatus[]>([])
  const [exporting, setExporting] = useState(false)
  const [exportError, setExportError] = useState<string | null>(null)

  const {
    control,
    handleSubmit,
    register,
    reset,
    formState: { errors },
  } = useForm<ProjectReportFilterValues>({
    resolver: zodResolver(projectReportFilterSchema),
    defaultValues: EMPTY_PROJECT_REPORT_FILTER,
  })

  useEffect(() => {
    let cancelled = false

    fetchProjectStatusReport(authFetch, applied)
      .then((report) => {
        if (!cancelled) {
          setLoaded({ filter: applied, report })
        }
      })
      .catch((error: unknown) => {
        if (!cancelled) {
          setLoaded({ filter: applied, error: apiErrorMessage(error, LOAD_FAILED) })
        }
      })

    return () => {
      cancelled = true
    }
  }, [authFetch, applied])

  // Loading is not a flag of its own: it is "what is on screen answers a
  // different filter to the one applied", which cannot go stale.
  const loading = loaded?.filter !== applied
  const current = loaded !== null && loaded.filter === applied ? loaded : null

  async function handleExport() {
    setExportError(null)
    setExporting(true)

    try {
      saveBlob(await downloadProjectStatusReportCsv(token, applied), projectReportFileName())
    } catch (error) {
      setExportError(apiErrorMessage(error, EXPORT_FAILED))
    } finally {
      setExporting(false)
    }
  }

  const filtered =
    applied.statuses.length > 0 || applied.from !== '' || applied.to !== ''

  return (
    <main className="mx-auto flex w-full max-w-7xl flex-col gap-6 px-4 py-8 sm:px-6 sm:py-10">
      <Card>
        <CardHeader>
          <CardTitle>Project status report</CardTitle>
          <CardDescription>
            Every project grouped by where it stands. Narrow it by status or by the day a project
            was submitted, then export what you see.
          </CardDescription>
        </CardHeader>

        <CardContent className="flex flex-col gap-6">
          <form
            aria-label="Report filters"
            className="flex flex-col gap-4"
            noValidate
            onSubmit={handleSubmit((values) => setApplied(values))}
          >
            <Controller
              control={control}
              name="statuses"
              render={({ field }) => (
                <FieldSet>
                  <FieldLegend variant="label">Status</FieldLegend>
                  <div className="flex flex-wrap gap-x-5 gap-y-2">
                    {PROJECT_STATUSES.map((status) => (
                      <label key={status} className="flex items-center gap-2 text-sm">
                        <input
                          type="checkbox"
                          className="size-4 accent-current"
                          checked={field.value.includes(status)}
                          onChange={(event) =>
                            field.onChange(
                              event.target.checked
                                ? [...field.value, status]
                                : field.value.filter((chosen) => chosen !== status),
                            )
                          }
                        />
                        {PROJECT_STATUS_LABELS[status]}
                      </label>
                    ))}
                  </div>
                  <p className="text-muted-foreground text-xs">
                    Leave all unticked to include every status.
                  </p>
                </FieldSet>
              )}
            />

            <div className="flex flex-col gap-3 sm:flex-row sm:items-start">
              <Field className="flex-1" data-invalid={errors.from ? true : undefined}>
                <FieldLabel htmlFor="report-from">Submitted from</FieldLabel>
                <Input
                  id="report-from"
                  type="date"
                  aria-invalid={errors.from ? true : undefined}
                  {...register('from')}
                />
                <FieldError errors={[errors.from]} />
              </Field>
              <Field className="flex-1" data-invalid={errors.to ? true : undefined}>
                <FieldLabel htmlFor="report-to">Submitted to</FieldLabel>
                <Input
                  id="report-to"
                  type="date"
                  aria-invalid={errors.to ? true : undefined}
                  {...register('to')}
                />
                <FieldError errors={[errors.to]} />
              </Field>
              <div className="flex gap-2 sm:pt-[1.625rem]">
                <Button type="submit" size="sm" disabled={loading}>
                  Apply filters
                </Button>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  disabled={loading || !filtered}
                  onClick={() => {
                    reset(EMPTY_PROJECT_REPORT_FILTER)
                    setApplied(EMPTY_PROJECT_REPORT_FILTER)
                  }}
                >
                  Clear
                </Button>
              </div>
            </div>
          </form>

          {current && 'error' in current && (
            <p role="alert" className="text-destructive text-sm">
              {current.error}
            </p>
          )}

          {loading && (
            <p role="status" className="text-muted-foreground text-sm">
              Loading the report…
            </p>
          )}

          {current && 'report' in current && (
            <>
              <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                <p data-testid="report-summary" className="text-sm">
                  <span className="font-medium">
                    {current.report.totalProjects}{' '}
                    {current.report.totalProjects === 1 ? 'project' : 'projects'}
                  </span>
                  <span className="text-muted-foreground">
                    {' '}
                    · total budget {formatAmount(current.report.totalBudget)}
                    {filtered && ' · filtered'}
                  </span>
                </p>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  disabled={exporting}
                  onClick={handleExport}
                >
                  {exporting ? 'Exporting…' : 'Export CSV'}
                </Button>
              </div>

              {exportError && (
                <p role="alert" className="text-destructive text-sm">
                  {exportError}
                </p>
              )}

              {current.report.totalProjects === 0 && (
                <p className="text-muted-foreground text-sm">
                  {filtered
                    ? 'No projects match these filters.'
                    : 'There are no projects yet.'}
                </p>
              )}

              <div className="flex flex-col gap-4">
                {current.report.groups.map((group) => {
                  const showingAll =
                    expanded.includes(group.status) || group.projects.length <= GROUP_PREVIEW_LIMIT
                  const visible = showingAll
                    ? group.projects
                    : group.projects.slice(0, GROUP_PREVIEW_LIMIT)

                  return (
                    <details
                      key={group.status}
                      open
                      data-testid={`group-${group.status}`}
                      className="rounded-lg border"
                    >
                      <summary className="flex cursor-pointer flex-wrap items-center gap-x-4 gap-y-1 px-4 py-3">
                        <StatusBadge status={group.status} />
                        <span className="text-sm font-medium">
                          {group.count} {group.count === 1 ? 'project' : 'projects'}
                        </span>
                        <span className="text-muted-foreground text-sm">
                          budget {formatAmount(group.totalBudget)}
                        </span>
                      </summary>

                      {group.projects.length === 0 ? (
                        <p className="text-muted-foreground border-t px-4 py-3 text-sm">
                          No projects in this status.
                        </p>
                      ) : (
                        <div className="overflow-x-auto border-t">
                          <Table>
                            <TableHeader>
                              <TableRow>
                                <TableHead>Project</TableHead>
                                <TableHead>Location</TableHead>
                                <TableHead className="text-right">Budget</TableHead>
                                <TableHead>Submitted</TableHead>
                              </TableRow>
                            </TableHeader>
                            <TableBody>
                              {visible.map((project) => (
                                <TableRow key={project.id}>
                                  <TableCell className="font-medium">
                                    <Link
                                      to={`/projects/${project.id}`}
                                      className="text-foreground underline underline-offset-4"
                                    >
                                      {project.name}
                                    </Link>
                                  </TableCell>
                                  <TableCell>{project.location}</TableCell>
                                  <TableCell className="text-right tabular-nums">
                                    {formatAmount(project.budget)}
                                  </TableCell>
                                  <TableCell className="tabular-nums">
                                    {submittedDay(project.createdAt)}
                                  </TableCell>
                                </TableRow>
                              ))}
                            </TableBody>
                          </Table>
                        </div>
                      )}

                      {!showingAll && (
                        <div className="flex flex-wrap items-center gap-3 border-t px-4 py-3">
                          <p className="text-muted-foreground text-sm">
                            Showing the newest {GROUP_PREVIEW_LIMIT} of {group.count}. The export has
                            all of them.
                          </p>
                          <Button
                            type="button"
                            variant="outline"
                            size="sm"
                            onClick={() => setExpanded((chosen) => [...chosen, group.status])}
                          >
                            Show all {group.count}
                          </Button>
                        </div>
                      )}
                    </details>
                  )
                })}
              </div>
            </>
          )}

          <p className="text-muted-foreground text-center text-sm">
            <Link to="/home" className="text-foreground underline underline-offset-4">
              Back to home
            </Link>
          </p>
        </CardContent>
      </Card>
    </main>
  )
}
