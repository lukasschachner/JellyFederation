import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Check, Mail, Plus, Send, Trash2, X } from 'lucide-react'
import { invitationsApi } from '../api/client'
import { loadConfig } from '../lib/config'
import type { Invitation } from '../api/types'
import { Badge } from '../components/Badge'
import { Button } from '../components/Button'
import { Card } from '../components/Card'
import { Input } from '../components/Input'

function statusBadge(status: Invitation['status']) {
  const map = {
    Pending: 'warning',
    Accepted: 'success',
    Declined: 'danger',
    Revoked: 'neutral',
  } as const
  return map[status]
}

function formatDate(iso: string) {
  return new Date(iso).toLocaleDateString(undefined, { dateStyle: 'medium' })
}

export function Invitations() {
  const cfg = loadConfig()
  const qc = useQueryClient()
  const [newServerId, setNewServerId] = useState('')
  const [sendError, setSendError] = useState('')

  const { data: invitations, isLoading } = useQuery({
    queryKey: ['invitations'],
    queryFn: invitationsApi.list,
  })

  const sendMutation = useMutation({
    mutationFn: (id: string) => invitationsApi.send(id),
    onSuccess: () => {
      setNewServerId('')
      setSendError('')
      qc.invalidateQueries({ queryKey: ['invitations'] })
    },
    onError: (err) => setSendError(err instanceof Error ? err.message : 'Failed to send'),
  })

  const respondMutation = useMutation({
    mutationFn: ({ id, accept }: { id: string; accept: boolean }) =>
      invitationsApi.respond(id, accept),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['invitations'] }),
  })

  const revokeMutation = useMutation({
    mutationFn: (id: string) => invitationsApi.revoke(id),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['invitations'] }),
  })

  const received = invitations?.filter(i => i.toServerId === cfg?.serverId) ?? []
  const sent = invitations?.filter(i => i.fromServerId === cfg?.serverId) ?? []

  return (
    <div className="max-w-3xl">
      <div className="mb-8">
        <h1 className="text-2xl font-semibold text-[var(--color-heading)]">Invitations</h1>
        <p className="text-sm text-[var(--color-text)] mt-1">
          Manage who can browse your library and whose library you can browse
        </p>
      </div>

      {/* Send invitation */}
      <Card className="mb-8">
        <h2 className="text-sm font-semibold text-[var(--color-heading)] mb-4 flex items-center gap-2">
          <Send size={14} className="text-[var(--color-accent)]" />
          Send Invitation
        </h2>
        <div className="flex gap-3">
          <Input
            placeholder="Server ID (xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx)"
            value={newServerId}
            onChange={e => setNewServerId(e.target.value)}
            error={sendError}
            className="flex-1"
          />
          <Button
            variant="primary"
            loading={sendMutation.isPending}
            disabled={!newServerId.trim()}
            onClick={() => sendMutation.mutate(newServerId.trim())}
          >
            <Plus size={14} />
            Send
          </Button>
        </div>
      </Card>

      {/* Received */}
      <section className="mb-8">
        <h2 className="text-sm font-semibold text-[var(--color-heading)] mb-3 flex items-center gap-2">
          <Mail size={14} />
          Received
          {received.length > 0 && (
            <span className="ml-auto text-xs text-[var(--color-text)] font-normal">{received.length}</span>
          )}
        </h2>

        {received.length === 0 && !isLoading && (
          <p className="text-sm text-[var(--color-text)]">No invitations received.</p>
        )}

        <div className="flex flex-col gap-2">
          {received.map(inv => (
            <Card key={inv.id} className="flex items-center gap-4">
              <div className="flex-1 min-w-0">
                <p className="text-sm text-[var(--color-heading)] font-medium">{inv.fromServerName}</p>
                <p className="text-xs text-[var(--color-text)] mt-0.5">{formatDate(inv.createdAt)}</p>
              </div>
              <Badge variant={statusBadge(inv.status)}>{inv.status}</Badge>
              {inv.status === 'Pending' && (
                <div className="flex gap-2">
                  <Button
                    size="sm"
                    variant="primary"
                    loading={respondMutation.isPending}
                    onClick={() => respondMutation.mutate({ id: inv.id, accept: true })}
                  >
                    <Check size={12} />
                    Accept
                  </Button>
                  <Button
                    size="sm"
                    variant="danger"
                    loading={respondMutation.isPending}
                    onClick={() => respondMutation.mutate({ id: inv.id, accept: false })}
                  >
                    <X size={12} />
                    Decline
                  </Button>
                </div>
              )}
            </Card>
          ))}
        </div>
      </section>

      {/* Sent */}
      <section>
        <h2 className="text-sm font-semibold text-[var(--color-heading)] mb-3 flex items-center gap-2">
          <Send size={14} />
          Sent
          {sent.length > 0 && (
            <span className="ml-auto text-xs text-[var(--color-text)] font-normal">{sent.length}</span>
          )}
        </h2>

        {sent.length === 0 && !isLoading && (
          <p className="text-sm text-[var(--color-text)]">No invitations sent.</p>
        )}

        <div className="flex flex-col gap-2">
          {sent.map(inv => (
            <Card key={inv.id} className="flex items-center gap-4">
              <div className="flex-1 min-w-0">
                <p className="text-sm text-[var(--color-heading)] font-medium">{inv.toServerName}</p>
                <p className="text-xs text-[var(--color-text)] mt-0.5">{formatDate(inv.createdAt)}</p>
              </div>
              <Badge variant={statusBadge(inv.status)}>{inv.status}</Badge>
              {inv.status === 'Pending' && (
                <Button
                  size="sm"
                  variant="ghost"
                  loading={revokeMutation.isPending}
                  onClick={() => revokeMutation.mutate(inv.id)}
                >
                  <Trash2 size={12} />
                  Revoke
                </Button>
              )}
            </Card>
          ))}
        </div>
      </section>
    </div>
  )
}
