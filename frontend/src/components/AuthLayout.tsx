import { ArrowLeft, Check } from 'lucide-react'
import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'

const PROMISES = [
  'One record for design, construction and payment',
  'Every screen scoped to the role that owns it',
  'A written history of every status change',
] as const

/**
 * The frame for the pages a visitor sees before they have a session — sign in,
 * register, and the two password-reset steps — in the same black-and-white
 * identity as the landing page and the signed-in app.
 *
 * On a wide screen the BuildNexus black takes the left of the window with the
 * mark and what the product promises; on a narrow one it shrinks to a strip
 * across the top so the form still has the whole width. The form itself is
 * whatever the page hands in, centred in the space that is left.
 */
export function AuthLayout({ children }: { children: ReactNode }) {
  return (
    <div className="grid min-h-svh lg:grid-cols-[5fr_6fr]">
      <aside className="relative hidden flex-col justify-between overflow-hidden bg-brand p-12 text-brand-foreground lg:flex">
        <Link
          to="/"
          className="flex w-fit items-center gap-2.5 rounded-lg outline-none focus-visible:ring-3 focus-visible:ring-white/40"
        >
          <img src="/logo-mark.png" alt="" className="h-9 w-auto brightness-0 invert" />
          <span className="font-heading text-base font-semibold tracking-tight">BuildNexus</span>
        </Link>

        <div className="flex flex-col items-center gap-10 py-10">
          <img src="/logo.png" alt="BuildNexus" className="h-72 w-auto brightness-0 invert" />

          <ul className="flex flex-col gap-3 text-sm text-brand-foreground/80">
            {PROMISES.map((promise) => (
              <li key={promise} className="flex items-start gap-2.5">
                <Check className="mt-0.5 size-4 shrink-0 text-brand-foreground" aria-hidden />
                {promise}
              </li>
            ))}
          </ul>
        </div>

        <p className="text-xs text-brand-foreground/50">
          © {new Date().getFullYear()} BuildNexus. Construction project management, end to end.
        </p>
      </aside>

      <div className="flex min-h-svh flex-col lg:min-h-0">
        <header className="bg-brand text-brand-foreground lg:hidden">
          <Link to="/" className="mx-auto flex h-14 max-w-sm items-center gap-2.5 px-6">
            <img src="/logo-mark.png" alt="" className="h-8 w-auto brightness-0 invert" />
            <span className="font-heading text-base font-semibold tracking-tight">BuildNexus</span>
          </Link>
        </header>

        <main className="flex flex-1 items-center justify-center p-6">
          <div className="w-full max-w-sm">{children}</div>
        </main>

        <footer className="px-6 pb-6 text-center text-sm text-muted-foreground">
          <Link
            to="/"
            className="inline-flex items-center gap-1.5 underline-offset-4 hover:text-foreground hover:underline"
          >
            <ArrowLeft className="size-3.5" aria-hidden />
            Back to the BuildNexus home page
          </Link>
        </footer>
      </div>
    </div>
  )
}
