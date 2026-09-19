import { describe, expect, it } from 'vitest'

import {
  DESIGN_MAX_FILE_BYTES,
  requestRevisionSchema,
  uploadDesignSchema,
} from './design-schemas'

function pdf(bytes = 1024): File {
  return new File([new Uint8Array(bytes)], 'plan.pdf', { type: 'application/pdf' })
}

describe('uploadDesignSchema', () => {
  it('accepts a document name and a valid PDF', () => {
    const result = uploadDesignSchema.safeParse({ name: 'GroundFloorPlan', revisionComment: '', file: pdf() })

    expect(result.success).toBe(true)
  })

  it('refuses a blank document name', () => {
    expect(uploadDesignSchema.safeParse({ name: '', revisionComment: '', file: pdf() }).success).toBe(false)
  })

  it('refuses a document name over 150 characters', () => {
    const result = uploadDesignSchema.safeParse({ name: 'x'.repeat(151), revisionComment: '', file: pdf() })

    expect(result.success).toBe(false)
  })

  it('accepts a blank revision comment — a first upload often has nothing to say', () => {
    expect(uploadDesignSchema.safeParse({ name: 'Plan', revisionComment: '', file: pdf() }).success).toBe(true)
  })

  it('refuses a revision comment over 2000 characters', () => {
    const result = uploadDesignSchema.safeParse({
      name: 'Plan', revisionComment: 'x'.repeat(2001), file: pdf(),
    })

    expect(result.success).toBe(false)
  })

  it('refuses an empty file', () => {
    const result = uploadDesignSchema.safeParse({ name: 'Plan', revisionComment: '', file: pdf(0) })

    expect(result.success).toBe(false)
  })

  it('refuses a file over the 10 MB limit', () => {
    const result = uploadDesignSchema.safeParse({
      name: 'Plan', revisionComment: '', file: pdf(DESIGN_MAX_FILE_BYTES + 1),
    })

    expect(result.success).toBe(false)
  })

  it('accepts a file exactly at the 10 MB limit', () => {
    const result = uploadDesignSchema.safeParse({
      name: 'Plan', revisionComment: '', file: pdf(DESIGN_MAX_FILE_BYTES),
    })

    expect(result.success).toBe(true)
  })

  it('refuses a file type outside PDF, JPG and PNG', () => {
    const file = new File([new Uint8Array(1024)], 'plan.gif', { type: 'image/gif' })

    expect(uploadDesignSchema.safeParse({ name: 'Plan', revisionComment: '', file }).success).toBe(false)
  })

  it('refuses a value that is not a File at all', () => {
    const result = uploadDesignSchema.safeParse({ name: 'Plan', revisionComment: '', file: 'not-a-file' })

    expect(result.success).toBe(false)
  })
})

describe('requestRevisionSchema', () => {
  it('accepts a non-empty comment', () => {
    expect(requestRevisionSchema.safeParse({ comment: 'Move the stairs.' }).success).toBe(true)
  })

  it('refuses a blank comment — unlike the upload form, this one is required', () => {
    expect(requestRevisionSchema.safeParse({ comment: '' }).success).toBe(false)
  })

  it('refuses a comment over 2000 characters', () => {
    expect(requestRevisionSchema.safeParse({ comment: 'x'.repeat(2001) }).success).toBe(false)
  })
})
