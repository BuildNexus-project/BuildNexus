import { vi } from 'vitest'

/** One recorded call to the stubbed `fetch`. */
export type RecordedRequest = {
  path: string
  method?: string
  /**
   * The JSON body, already parsed — or, for a multipart upload, its fields as a
   * plain object (a `File` field stays a `File`).
   */
  body: unknown
}

/**
 * A stand-in for one API response, shaped the way `apiFetch` reads it: the
 * status, and a body it will `JSON.parse` out of `text()`.
 */
export function apiResponse(status: number, body: unknown): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    text: () => Promise.resolve(JSON.stringify(body)),
  } as unknown as Response
}

/**
 * A stand-in for a binary response — a file download, which goes straight
 * through `fetch` rather than the JSON client and reads `blob()` instead of
 * `text()`.
 */
export function fileResponse(status: number, blob: Blob): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    text: () => Promise.resolve(''),
    blob: () => Promise.resolve(blob),
  } as unknown as Response
}

/** A `FormData`'s fields as a plain object, for asserting on a multipart request. */
function formDataToObject(form: FormData): Record<string, FormDataEntryValue> {
  const object: Record<string, FormDataEntryValue> = {}
  form.forEach((value, key) => {
    object[key] = value
  })
  return object
}

/**
 * Replaces `fetch` for the duration of a test and records what the page asked
 * for.
 *
 * The pages under test call the real `apiFetch`, so stubbing at this level
 * keeps the request building, the problem-details parsing and the `ApiError`
 * mapping in the test rather than mocked past — which is where the interesting
 * behaviour is: a 400 with no field errors means something different to the
 * reset page than a 400 that names one.
 *
 * Undo it with `vi.unstubAllGlobals()`.
 */
export function stubFetch(...responses: Array<Response | Error>): RecordedRequest[] {
  const requests: RecordedRequest[] = []
  let call = 0

  vi.stubGlobal('fetch', (path: string, init: RequestInit = {}) => {
    requests.push({
      path,
      method: init.method,
      body:
        typeof init.body === 'string'
          ? JSON.parse(init.body)
          : init.body instanceof FormData
            ? formDataToObject(init.body)
            : undefined,
    })

    // The last response stands in for every call past it, so a test that only
    // cares about one answer does not have to say how many times it is given.
    const answer = responses[Math.min(call++, responses.length - 1)]

    return answer instanceof Error ? Promise.reject(answer) : Promise.resolve(answer)
  })

  return requests
}
