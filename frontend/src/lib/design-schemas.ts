import { z } from 'zod'

/**
 * The upload limits, mirrored from the Design Service's own
 * `DesignFileValidator`, so most mistakes are caught before a request is made.
 * The service revalidates everything — and decides the type from the file's own
 * bytes, not the `type` the browser guessed — so its answer is the one that
 * counts.
 */
export const DESIGN_MAX_FILE_BYTES = 10 * 1024 * 1024

/** The three types US-09 allows, as their MIME strings. */
export const DESIGN_ACCEPTED_MIME_TYPES = ['application/pdf', 'image/jpeg', 'image/png'] as const

/** For the file input's `accept` attribute — extensions and MIME types both. */
export const DESIGN_FILE_ACCEPT = '.pdf,.jpg,.jpeg,.png,application/pdf,image/jpeg,image/png'

/**
 * The new-upload form: which document to file it under, an optional note on what
 * changed, and the file.
 */
export const uploadDesignSchema = z.object({
  name: z
    .string()
    .trim()
    .min(1, 'A document name is required.')
    .max(150, 'Document name must not exceed 150 characters.'),
  /** Blank is allowed and sent as nothing. */
  revisionComment: z
    .string()
    .trim()
    .max(2000, 'Revision comment must not exceed 2000 characters.'),
  file: z
    .instanceof(File, { message: 'Choose a PDF, JPG or PNG file to upload.' })
    .refine((file) => file.size > 0, 'The file is empty.')
    .refine(
      (file) => file.size <= DESIGN_MAX_FILE_BYTES,
      'The file is larger than the 10 MB limit.',
    )
    .refine(
      (file) => (DESIGN_ACCEPTED_MIME_TYPES as readonly string[]).includes(file.type),
      'Only PDF, JPG and PNG files are accepted.',
    ),
})

export type UploadDesignValues = z.infer<typeof uploadDesignSchema>
