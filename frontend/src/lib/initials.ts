/** "Ada Perera" → "AP". First and last word, so a middle name does not crowd the circle. */
export function initialsOf(fullName: string): string {
  const words = fullName.trim().split(/\s+/).filter(Boolean)

  if (words.length === 0) {
    return '?'
  }

  const first = words[0][0]
  const last = words.length > 1 ? words[words.length - 1][0] : ''

  return `${first}${last}`.toUpperCase()
}
