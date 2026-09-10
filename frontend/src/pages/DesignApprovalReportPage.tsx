import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'

import { useAuth } from '@/auth/auth-context'
import { Button } from '@/components/ui/button'
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
import { fetchDesignApprovalReport, type DesignApprovalReportRow } from '@/lib/design-api'

const LOAD_FAILED = 'Could not load the design approval report.'

/** A count that is only meaningful once something is approved. */
function formatAverage(value: number | null): string {
  return value === null ? '—' : value.toFixed(1)
}

/** Hours as a reader would say them: minutes under an hour, hours under two days, days beyond. */
function formatDuration(hours: number | null): string {
  if (hours === null) {
    return '—'
  }

  if (hours < 1) {
    return `${Math.round(hours * 60)} min`
  }

  if (hours < 48) {
    return `${hours.toFixed(1)} h`
  }

  const days = Math.floor(hours / 24)
  const remainder = Math.round(hours - days * 24)

  return remainder === 0 ? `${days} d` : `${days} d ${remainder} h`
}

/**
 * The design approval report (US-20): one row per project that has any design
 * document, showing how much revision its designs went through and how long
 * they took to approve.
 *
 * Admin only — the route guards it, and the endpoint behind it answers 403 to
 * anyone else.
 */
export function DesignApprovalReportPage() {
  const { authFetch } = useAuth()

  const [rows, setRows] = useState<DesignApprovalReportRow[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false

    fetchDesignApprovalReport(authFetch)
      .then((loaded) => {
        if (!cancelled) {
          setRows(loaded)
        }
      })
      .catch((error: unknown) => {
        if (!cancelled) {
          setLoadError(apiErrorMessage(error, LOAD_FAILED))
        }
      })

    return () => {
      cancelled = true
    }
  }, [authFetch])

  if (loadError) {
    return (
      <main className="mx-auto flex min-h-svh w-full max-w-2xl flex-col justify-center gap-4 p-6">
        <Card>
          <CardHeader>
            <CardTitle>This report is not available to you</CardTitle>
            <CardDescription role="alert">{loadError}</CardDescription>
          </CardHeader>
          <CardContent>
            <Button render={<Link to="/home" />} variant="outline" className="w-full">
              Back to home
            </Button>
          </CardContent>
        </Card>
      </main>
    )
  }

  return (
    <main className="mx-auto flex min-h-svh w-full max-w-4xl flex-col justify-center gap-4 p-6">
      <Card>
        <CardHeader>
          <CardTitle>Design approval report</CardTitle>
          <CardDescription>
            One row per project with design documents. “Versions to approval” and “Time to
            approval” are averaged over the project’s approved documents — a project with none
            approved yet shows a dash, and its version count is what has piled up so far.
          </CardDescription>
        </CardHeader>

        <CardContent className="flex flex-col gap-4">
          {rows === null ? (
            <p className="text-muted-foreground text-sm">Loading the report…</p>
          ) : rows.length === 0 ? (
            <p className="text-muted-foreground text-sm">
              No project has any design documents yet.
            </p>
          ) : (
            <div className="overflow-x-auto">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Project</TableHead>
                    <TableHead>Documents (approved / total)</TableHead>
                    <TableHead>Total versions</TableHead>
                    <TableHead>Avg versions to approval</TableHead>
                    <TableHead>Avg time to approval</TableHead>
                  </TableRow>
                </TableHeader>

                <TableBody>
                  {rows.map((row) => (
                    <TableRow key={row.projectId}>
                      <TableCell className="font-medium">
                        <Link
                          to={`/projects/${row.projectId}`}
                          className="text-foreground underline underline-offset-4"
                        >
                          {row.projectId}
                        </Link>
                      </TableCell>
                      <TableCell>
                        {row.approvedDocumentCount} / {row.documentCount}
                      </TableCell>
                      <TableCell>{row.totalVersionCount}</TableCell>
                      <TableCell>{formatAverage(row.averageVersionsToApproval)}</TableCell>
                      <TableCell>{formatDuration(row.averageHoursToApproval)}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>
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
