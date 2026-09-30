import { ChevronRight, House } from 'lucide-react'
import { Fragment } from 'react'
import { Link, useLocation } from 'react-router-dom'

import { useAuth } from '@/auth/auth-context'
import { breadcrumbsFor, WORKSPACE_LABELS } from '@/lib/nav'

/**
 * The strip under the header that says where the user is and how to get back:
 * a breadcrumb trail from Home to the current page, and which role's workspace
 * they are in.
 *
 * It says where and not what — the page's own heading says what — so nothing
 * here repeats a title the page already shows. Absent on the dashboard, which is
 * the top of the trail, and on any page with no trail.
 */
export function PageBar() {
  const { user } = useAuth()
  const { pathname } = useLocation()

  const trail = breadcrumbsFor(pathname)

  if (!user || trail.length === 0) {
    return null
  }

  return (
    <div className="border-b bg-background">
      <div className="mx-auto flex min-h-11 w-full max-w-7xl flex-wrap items-center justify-between gap-x-4 gap-y-1 px-4 py-2 sm:px-6">
        <nav aria-label="Breadcrumb">
          <ol className="flex flex-wrap items-center gap-1.5 text-sm text-muted-foreground">
            <li>
              <Link
                to="/home"
                className="inline-flex items-center gap-1.5 rounded-md hover:text-foreground"
              >
                <House className="size-3.5" aria-hidden />
                Home
              </Link>
            </li>

            {trail.map((crumb, index) => {
              const isLast = index === trail.length - 1

              return (
                <Fragment key={`${index}-${crumb.label}`}>
                  <li aria-hidden className="flex items-center">
                    <ChevronRight className="size-3.5" />
                  </li>
                  <li>
                    {crumb.to && !isLast ? (
                      <Link to={crumb.to} className="rounded-md hover:text-foreground">
                        {crumb.label}
                      </Link>
                    ) : (
                      <span aria-current={isLast ? 'page' : undefined} className="text-foreground">
                        {crumb.label}
                      </span>
                    )}
                  </li>
                </Fragment>
              )
            })}
          </ol>
        </nav>

        <span className="text-xs font-medium tracking-wide text-muted-foreground uppercase">
          {WORKSPACE_LABELS[user.role]}
        </span>
      </div>
    </div>
  )
}
