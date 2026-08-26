import { cleanup } from '@testing-library/react'
import { afterEach } from 'vitest'

// jest-dom's matchers (toBeInTheDocument, toHaveTextContent, ...) on Vitest's expect.
import '@testing-library/jest-dom/vitest'

// Testing Library only registers its own cleanup when the test globals are
// injected, and they are not here — every test imports what it uses. So the
// rendered tree is torn down by hand, and the stored token with it, or one
// test's signed-in user would still be signed in for the next.
afterEach(() => {
  cleanup()
  localStorage.clear()
})
