import { AlertCircle } from 'lucide-react'
import { ApiClientError, getErrorMessage, getErrorMetadata } from '../api/client'
import { CopyButton } from './CopyButton'

interface ErrorDetailsProps {
  error: unknown
  title?: string
  compact?: boolean
  className?: string
}

export function ErrorDetails({ error, title = 'Something went wrong', compact = false, className = '' }: ErrorDetailsProps) {
  const message = getErrorMessage(error, 'Unexpected error')
  const metadata = getErrorMetadata(error)
  const apiError = error instanceof ApiClientError ? error : null
  const details = [
    apiError?.status ? `HTTP ${apiError.status} ${apiError.statusText}` : undefined,
    metadata,
  ].filter(Boolean).join('\n')

  return (
    <div className={`rounded-xl border border-red-500/20 bg-[var(--color-danger-bg)] p-4 ${className}`}>
      <div className="flex items-start gap-3">
        <div className="mt-0.5 flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-red-500/15 text-red-400">
          <AlertCircle size={16} />
        </div>
        <div className="min-w-0 flex-1">
          {!compact && <p className="text-sm font-medium text-[var(--color-heading)]">{title}</p>}
          <p className="text-sm leading-6 text-red-200/90">{message}</p>
          {details && (
            <details className="mt-2 text-xs text-[var(--color-text)]">
              <summary className="cursor-pointer select-none hover:text-[var(--color-heading)]">Technical details</summary>
              <pre className="mt-2 whitespace-pre-wrap break-words rounded-lg bg-black/20 p-3 font-mono leading-5">{details}</pre>
              <div className="mt-2 flex flex-wrap gap-2">
                {apiError?.correlationId && <CopyButton value={apiError.correlationId} label="Copy correlation ID" successMessage="Correlation ID copied" />}
                <CopyButton value={`${message}${details ? `\n\n${details}` : ''}`} label="Copy details" successMessage="Error details copied" />
              </div>
            </details>
          )}
        </div>
      </div>
    </div>
  )
}
