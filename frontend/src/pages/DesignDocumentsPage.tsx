import { useEffect, useRef, useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { Link, useParams } from 'react-router-dom'

import { useAuth } from '@/auth/auth-context'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldDescription, FieldError, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
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
import { apiErrorMessage } from '@/lib/api'
import {
  downloadDesignVersionFile,
  fetchProjectDesignDocuments,
  uploadDesignDocument,
  type DesignDocument,
  type DesignVersion,
} from '@/lib/design-api'
import { DESIGN_FILE_ACCEPT, uploadDesignSchema, type UploadDesignValues } from '@/lib/design-schemas'
import { applyApiErrorToForm } from '@/lib/form-errors'

const LOAD_FAILED = 'Could not load this project’s design documents.'

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

/**
 * Saves a downloaded file to disk through a throwaway link — the same trick
 * every plain web app uses, since there is no other way to hand the browser
 * bytes it already fetched.
 */
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

/** One version's row: its metadata and a button to fetch its file. */
function VersionRow({
  version,
  onDownload,
  isDownloading,
}: {
  version: DesignVersion
  onDownload: (version: DesignVersion) => void
  isDownloading: boolean
}) {
  return (
    <TableRow>
      <TableCell className="font-medium">{version.displayName}</TableCell>
      <TableCell>{formatMoment(version.uploadedAt)}</TableCell>
      <TableCell className="text-muted-foreground text-xs">{version.uploadedBy}</TableCell>
      <TableCell>
        <Badge variant="secondary">{version.status}</Badge>
      </TableCell>
      <TableCell className="text-muted-foreground max-w-56 text-sm text-wrap">
        {version.revisionComment ?? '—'}
      </TableCell>
      <TableCell>
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={() => onDownload(version)}
          disabled={isDownloading}
        >
          {isDownloading ? 'Downloading…' : 'Download'}
        </Button>
      </TableCell>
    </TableRow>
  )
}

/**
 * A project's design documents: every version an Architect has uploaded, and —
 * for an Architect — the form to add the next one (US-09).
 *
 * Open to every role that may see the project: which project a caller may
 * actually open is a per-project question the Design Service answers by asking
 * the Project Service, the same way {@link ProjectDetailPage} works. Somebody
 * who is not on the project gets a 403 with a reason, shown here the same way.
 */
export function DesignDocumentsPage() {
  const { projectId } = useParams<{ projectId: string }>()
  const { authFetch, user, token } = useAuth()

  const [documents, setDocuments] = useState<DesignDocument[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [uploadError, setUploadError] = useState<string | null>(null)
  const [downloadError, setDownloadError] = useState<string | null>(null)
  const [downloadingId, setDownloadingId] = useState<string | null>(null)

  const isArchitect = user?.role === 'Architect'
  const fileInputRef = useRef<HTMLInputElement>(null)

  const {
    register,
    handleSubmit,
    reset,
    setError,
    setValue,
    formState: { errors, isSubmitting },
  } = useForm<UploadDesignValues>({
    resolver: zodResolver(uploadDesignSchema),
    defaultValues: { name: '', revisionComment: '' },
  })

  useEffect(() => {
    if (!projectId) {
      return
    }

    let cancelled = false

    fetchProjectDesignDocuments(authFetch, projectId)
      .then((loaded) => {
        if (!cancelled) {
          setDocuments(loaded)
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
  }, [authFetch, projectId])

  /** Re-reads the list after an upload — not cancellation-guarded, since it only runs from a button on a page that is still mounted. */
  async function reload(id: string) {
    try {
      setDocuments(await fetchProjectDesignDocuments(authFetch, id))
      setLoadError(null)
    } catch (error) {
      setLoadError(apiErrorMessage(error, LOAD_FAILED))
    }
  }

  async function onUpload(values: UploadDesignValues) {
    if (!projectId) {
      return
    }

    setUploadError(null)

    try {
      await uploadDesignDocument(authFetch, projectId, values)

      reset({ name: '', revisionComment: '' })
      // React cannot set a file input's value, so it is cleared by hand.
      if (fileInputRef.current) {
        fileInputRef.current.value = ''
      }

      await reload(projectId)
    } catch (error) {
      setUploadError(
        applyApiErrorToForm(error, setError, 'Could not upload this file. Please try again.'),
      )
    }
  }

  async function handleDownload(version: DesignVersion) {
    setDownloadError(null)
    setDownloadingId(version.id)

    try {
      saveBlob(await downloadDesignVersionFile(token, version.id), version.fileName)
    } catch (error) {
      setDownloadError(apiErrorMessage(error, 'Could not download this file. Please try again.'))
    } finally {
      setDownloadingId(null)
    }
  }

  if (loadError) {
    return (
      <main className="mx-auto flex min-h-svh w-full max-w-2xl flex-col justify-center gap-4 p-6">
        <Card>
          <CardHeader>
            <CardTitle>These design documents are not available to you</CardTitle>
            <CardDescription role="alert">{loadError}</CardDescription>
          </CardHeader>

          <CardContent>
            <Button render={<Link to={`/projects/${projectId ?? ''}`} />} variant="outline" className="w-full">
              Back to the project
            </Button>
          </CardContent>
        </Card>
      </main>
    )
  }

  return (
    <main className="mx-auto flex min-h-svh w-full max-w-3xl flex-col justify-center gap-4 p-6">
      <Card>
        <CardHeader>
          <CardTitle>Design documents</CardTitle>
          <CardDescription>
            Every revision an Architect has uploaded, with the latest version at the bottom of
            each list.
          </CardDescription>
        </CardHeader>

        <CardContent className="flex flex-col gap-6">
          {documents === null ? (
            <p className="text-muted-foreground text-sm">Loading design documents…</p>
          ) : documents.length === 0 ? (
            <p className="text-muted-foreground text-sm">
              No design documents have been uploaded for this project yet.
            </p>
          ) : (
            <div className="flex flex-col gap-6">
              {documents.map((document) => (
                <section key={document.id} className="flex flex-col gap-2">
                  <h2 className="text-sm font-medium">{document.name}</h2>

                  <Table>
                    <TableHeader>
                      <TableRow>
                        <TableHead>Version</TableHead>
                        <TableHead>Uploaded</TableHead>
                        <TableHead>Uploaded by</TableHead>
                        <TableHead>Status</TableHead>
                        <TableHead>Comment</TableHead>
                        <TableHead />
                      </TableRow>
                    </TableHeader>

                    <TableBody>
                      {/* Oldest first, exactly as the service sent it — the
                          latest revision is the last row. */}
                      {document.versions.map((version) => (
                        <VersionRow
                          key={version.id}
                          version={version}
                          onDownload={handleDownload}
                          isDownloading={downloadingId === version.id}
                        />
                      ))}
                    </TableBody>
                  </Table>
                </section>
              ))}
            </div>
          )}

          {downloadError && (
            <p role="alert" className="text-destructive text-sm">
              {downloadError}
            </p>
          )}

          {isArchitect && (
            <>
              <Separator />

              <section className="flex flex-col gap-3">
                <h2 className="text-sm font-medium">Upload a document</h2>

                <form
                  onSubmit={handleSubmit(onUpload)}
                  noValidate
                  className="flex flex-col gap-4"
                >
                  <Field>
                    <FieldLabel htmlFor="name">Document name</FieldLabel>
                    <Input id="name" aria-invalid={Boolean(errors.name)} {...register('name')} />
                    <FieldDescription>
                      Reuse an existing name (e.g. GroundFloorPlan) to add the next version to it.
                    </FieldDescription>
                    <FieldError errors={[errors.name]} />
                  </Field>

                  <Field>
                    <FieldLabel htmlFor="revisionComment">Revision comment</FieldLabel>
                    <Textarea
                      id="revisionComment"
                      rows={3}
                      aria-invalid={Boolean(errors.revisionComment)}
                      {...register('revisionComment')}
                    />
                    <FieldDescription>Optional. What changed in this upload.</FieldDescription>
                    <FieldError errors={[errors.revisionComment]} />
                  </Field>

                  <Field>
                    <FieldLabel htmlFor="file">File</FieldLabel>
                    <Input
                      id="file"
                      type="file"
                      accept={DESIGN_FILE_ACCEPT}
                      ref={fileInputRef}
                      aria-invalid={Boolean(errors.file)}
                      onChange={(event) => {
                        const file = event.target.files?.[0]
                        // Cast: RHF's generated type says File, but nothing was
                        // picked is a real possibility — zod's own check catches
                        // that and reports it as "choose a file".
                        setValue('file', file as File, { shouldValidate: true })
                      }}
                    />
                    <FieldDescription>PDF, JPG or PNG, up to 10 MB.</FieldDescription>
                    <FieldError errors={[errors.file]} />
                  </Field>

                  {uploadError && (
                    <p role="alert" className="text-destructive text-sm">
                      {uploadError}
                    </p>
                  )}

                  <Button type="submit" disabled={isSubmitting}>
                    {isSubmitting ? 'Uploading…' : 'Upload'}
                  </Button>
                </form>
              </section>
            </>
          )}

          <p className="text-muted-foreground text-center text-sm">
            <Link
              to={`/projects/${projectId ?? ''}`}
              className="text-foreground underline underline-offset-4"
            >
              Back to the project
            </Link>
          </p>
        </CardContent>
      </Card>
    </main>
  )
}
