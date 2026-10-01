import { useEffect, useState } from 'react'

import { useAuth } from '@/auth/auth-context'
import { ApiError } from '@/lib/api'

/** The token-attaching fetch the auth context hands out. */
type AuthFetch = ReturnType<typeof useAuth>['authFetch']

/**
 * Where one slice of a dashboard has got to.
 *
 * A dashboard is several requests to several services, and they do not succeed or fail
 * together. So each slice carries its own state, and the page shows what it has: a slow
 * or failed slice leaves its own card saying so, and the rest of the dashboard is
 * unaffected.
 *
 * A failure says two things. `message` is always the loader's own sentence naming the slice
 * that failed — a bare "An error occurred" from a service would leave a dashboard of
 * several slices not saying which one. `detail` is the reason the service gave, when it
 * gave one worth showing, and is what to read after `message`.
 */
export type SliceState<T> =
  | { status: 'loading' }
  | { status: 'ready'; data: T }
  | { status: 'error'; message: string; detail?: string }

const DEFAULT_FAILURE = 'This could not be loaded right now.'

/**
 * Loads one dashboard slice when the component mounts, and reports where it stands.
 *
 * The reason a service gave for a refusal is kept as `detail`, so a 502 saying the projects
 * could not be looked up reaches the person rather than being flattened into "something
 * went wrong". Only the `detail` member is kept: a problem's `title` is a label for the
 * kind of failure ("Bad Gateway"), and shown alone it says nothing the slice's own message
 * has not. Anything that is not a service answer — a dropped connection — has no detail.
 *
 * `load` should be a stable reference (one of the fetchers in `dashboard-api`, not an
 * inline arrow), since a new function on every render would ask for the slice again on
 * every render.
 */
export function useDashboardSlice<T>(
  load: (authFetch: AuthFetch) => Promise<T>,
  failureMessage: string = DEFAULT_FAILURE,
): SliceState<T> {
  const { authFetch } = useAuth()
  const [state, setState] = useState<SliceState<T>>({ status: 'loading' })

  useEffect(() => {
    let cancelled = false

    load(authFetch)
      .then((data) => {
        if (!cancelled) {
          setState({ status: 'ready', data })
        }
      })
      .catch((error: unknown) => {
        if (!cancelled) {
          setState({
            status: 'error',
            message: failureMessage,
            detail: error instanceof ApiError ? error.detail : undefined,
          })
        }
      })

    // The effect can outlive the page if the user navigates away mid-request.
    return () => {
      cancelled = true
    }
  }, [authFetch, load, failureMessage])

  return state
}
