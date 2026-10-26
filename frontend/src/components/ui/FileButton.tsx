import { useRef, type ReactNode } from 'react'
import { buttonClass, type ButtonSize, type ButtonVariant } from './button-styles'
import { Spinner } from './Spinner'

type FileButtonProps = {
  /** Accessible, visible label of the button. */
  children: ReactNode
  accept?: string
  onFile: (file: File) => void
  variant?: ButtonVariant
  size?: ButtonSize
  loading?: boolean
  disabled?: boolean
  'aria-describedby'?: string
  className?: string
}

/** A real <button> that opens the file picker (hidden <input type="file">). */
export function FileButton({ children, accept, onFile, variant = 'secondary', size = 'md', loading, disabled, className, ...aria }: FileButtonProps) {
  const inputRef = useRef<HTMLInputElement>(null)
  return (
    <>
      <button
        type="button"
        disabled={disabled || loading}
        aria-busy={loading || undefined}
        onClick={() => inputRef.current?.click()}
        className={buttonClass(variant, size, className)}
        {...aria}
      >
        {loading ? <Spinner /> : null}
        {children}
      </button>
      <input
        ref={inputRef}
        type="file"
        accept={accept}
        tabIndex={-1}
        aria-hidden="true"
        className="sr-only"
        onChange={(e) => {
          const file = e.target.files?.[0]
          e.target.value = ''
          if (file) onFile(file)
        }}
      />
    </>
  )
}
