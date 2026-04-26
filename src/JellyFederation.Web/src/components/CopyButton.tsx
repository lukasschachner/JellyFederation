import { useState } from 'react'
import { Check, Copy } from 'lucide-react'
import { useToast } from '../hooks/useToast'

interface CopyButtonProps {
  value: string | null | undefined
  label?: string
  successMessage?: string
  disabled?: boolean
  className?: string
}

async function copyText(value: string) {
  if (navigator.clipboard?.writeText) {
    await navigator.clipboard.writeText(value)
    return
  }

  const textarea = document.createElement('textarea')
  textarea.value = value
  textarea.setAttribute('readonly', '')
  textarea.style.position = 'fixed'
  textarea.style.left = '-9999px'
  document.body.appendChild(textarea)
  textarea.select()
  try {
    document.execCommand('copy')
  } finally {
    document.body.removeChild(textarea)
  }
}

export function CopyButton({
  value,
  label = 'Copy',
  successMessage = 'Copied to clipboard',
  disabled,
  className = '',
}: CopyButtonProps) {
  const toast = useToast()
  const [copied, setCopied] = useState(false)
  const canCopy = Boolean(value) && !disabled

  async function handleCopy() {
    if (!value) return
    try {
      await copyText(value)
      setCopied(true)
      toast.success(successMessage)
      window.setTimeout(() => setCopied(false), 1400)
    } catch (err) {
      toast.error('Copy failed', err instanceof Error ? err.message : undefined)
    }
  }

  return (
    <button
      type="button"
      onClick={handleCopy}
      disabled={!canCopy}
      title={label}
      aria-label={label}
      className={`inline-flex h-8 min-w-8 items-center justify-center gap-1.5 rounded-lg border border-[var(--color-border)] bg-white/[0.03] px-2 text-xs font-medium text-[var(--color-text)] transition-all duration-150 hover:border-[var(--color-accent)]/50 hover:bg-[var(--color-accent-dim)] hover:text-[var(--color-heading)] active:scale-95 disabled:cursor-not-allowed disabled:opacity-40 ${className}`}
    >
      {copied ? <Check size={13} className="text-emerald-400" /> : <Copy size={13} />}
      <span className="hidden sm:inline">{copied ? 'Copied' : label}</span>
    </button>
  )
}
