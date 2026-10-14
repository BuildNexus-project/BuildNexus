import { useId, type ComponentProps } from 'react'

import { Input } from '@/components/ui/input'
import { cn } from '@/lib/utils'

type PhoneNumberInputProps = Omit<ComponentProps<'input'>, 'value' | 'defaultValue' | 'onChange'> & {
  value: string
  onValueChange: (value: string) => void
}

function nationalNumber(value: string) {
  if (value.startsWith('+94')) return value.slice(3)
  if (value.startsWith('0')) return value.slice(1)
  return value
}

export function PhoneNumberInput({ value, onValueChange, className, ...props }: PhoneNumberInputProps) {
  const prefixId = useId()

  return (
    <div className="relative">
      <span id={prefixId} className="pointer-events-none absolute inset-y-0 left-2.5 flex select-none items-center text-sm text-muted-foreground">
        +94
      </span>
      <Input
        {...props}
        type="tel"
        inputMode="numeric"
        autoComplete="tel-national"
        maxLength={9}
        minLength={9}
        pattern="7[0-9]{8}"
        aria-describedby={[prefixId, props['aria-describedby']].filter(Boolean).join(' ')}
        className={cn('pl-12', className)}
        value={nationalNumber(value)}
        onChange={(event) => {
          const digits = event.target.value
          if (/^\d{0,9}$/.test(digits)) {
            onValueChange(digits ? `+94${digits}` : '')
          }
        }}
        onPaste={(event) => {
          const pasted = event.clipboardData.getData('text').trim()
          if (/^(?:07\d{8}|\+947\d{8})$/.test(pasted)) {
            event.preventDefault()
            onValueChange(`+94${nationalNumber(pasted)}`)
          } else if (!/^\d{1,9}$/.test(pasted)) {
            event.preventDefault()
          }
        }}
      />
    </div>
  )
}
