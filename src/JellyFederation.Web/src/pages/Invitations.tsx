import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Check, Inbox, Mail, Plus, Send, Trash2, X } from 'lucide-react'
import { getErrorDescription, getErrorMessage, invitationsApi } from '../api/client'
import { formatDate } from '../utils/formatDate'
import type { Invitation } from '../api/types'
import { useConfig } from '../hooks/useConfig'
import { Button } from '../components/Button'
import { Card } from '../components/Card'
import { ConfirmDialog } from '../components/ConfirmDialog'
import { EmptyState } from '../components/EmptyState'
import { Input } from '../components/Input'
import { PageHeader } from '../components/PageHeader'
import { SectionHeader } from '../components/SectionHeader'
import { StatusPill } from '../components/StatusPill'
import { useToast } from '../hooks/useToast'

export function Invitations() {
  const cfg = useConfig()
  const qc = useQueryClient()
  const toast = useToast()
  const [newServerId, setNewServerId] = useState('')
  const [sendError, setSendError] = useState('')
  const [invitationToRevoke, setInvitationToRevoke] = useState<Invitation | null>(null)
  const [respondingIds, setRespondingIds] = useState<Set<string>>(new Set())
  const [revokingIds, setRevokingIds] = useState<Set<string>>(new Set())
  const myServerId = cfg?.serverId?.toLowerCase() ?? ''

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
      toast.success('Invitation sent')
    },
    onError: (err) => {
      const msg = getErrorMessage(err, 'Failed to send')
      setSendError(msg)
      toast.error('Could not send invitation', getErrorDescription(err, 'Failed to send'))
    },
  })

  const respondMutation = useMutation({
    mutationFn: ({ id, accept }: { id: string; accept: boolean }) => invitationsApi.respond(id, accept),
    onMutate: ({ id }) => setRespondingIds(prev => new Set(prev).add(id)),
    onSuccess: (_data, vars) => {
      qc.invalidateQueries({ queryKey: ['invitations'] })
      toast.success(vars.accept ? 'Invitation accepted' : 'Invitation declined')
    },
    onError: (err) => toast.error('Could not update invitation', getErrorDescription(err, 'Failed to update invitation')),
    onSettled: (_data, _err, vars) => setRespondingIds(prev => { const next = new Set(prev); next.delete(vars.id); return next }),
  })

  const revokeMutation = useMutation({
    mutationFn: (id: string) => invitationsApi.revoke(id),
    onMutate: (id) => setRevokingIds(prev => new Set(prev).add(id)),
    onSuccess: () => {
      setInvitationToRevoke(null)
      qc.invalidateQueries({ queryKey: ['invitations'] })
      toast.info('Invitation revoked')
    },
    onError: (err) => toast.error('Could not revoke invitation', getErrorDescription(err, 'Failed to revoke invitation')),
    onSettled: (_data, _err, id) => setRevokingIds(prev => { const next = new Set(prev); next.delete(id); return next }),
  })

  const received = invitations?.filter(i => i.toServerId.toLowerCase() === myServerId) ?? []
  const sent = invitations?.filter(i => i.fromServerId.toLowerCase() === myServerId) ?? []

  return (
    <div className="max-w-3xl">
      <PageHeader
        eyebrow="Federation"
        title="Invitations"
        description="Manage who can browse your library and whose library you can browse. Accepted invitations unlock library discovery."
        icon={<Mail size={18} />}
      />

      <Card className="mb-8 bg-gradient-to-b from-white/[0.03] to-transparent">
        <SectionHeader title="Send Invitation" icon={<Send size={14} />} description="Paste another server ID to start federation with that peer." />
        <div className="flex flex-col gap-3 sm:flex-row">
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

      <section className="mb-8">
        <SectionHeader title="Received" icon={<Inbox size={14} />} count={received.length || undefined} />

        {received.length === 0 && !isLoading && (
          <EmptyState
            icon={<Inbox size={18} />}
            title="No invitations received"
            description="Incoming invitations from other federated servers will appear here."
            className="py-10"
          />
        )}

        <div className="flex flex-col gap-2">
          {received.map(inv => (
            <Card key={inv.id} variant="interactive" className="flex flex-col gap-3 sm:flex-row sm:items-center sm:gap-4">
              <div className="flex-1 min-w-0">
                <p className="text-sm text-[var(--color-heading)] font-medium truncate">{inv.fromServerName}</p>
                <p className="text-xs text-[var(--color-text)] mt-0.5">{formatDate(inv.createdAt)}</p>
              </div>
              <StatusPill status={inv.status} />
              {inv.status === 'Pending' && (
                <div className="flex gap-2">
                  <Button size="sm" variant="primary" loading={respondingIds.has(inv.id)} onClick={() => respondMutation.mutate({ id: inv.id, accept: true })}>
                    <Check size={12} />
                    Accept
                  </Button>
                  <Button size="sm" variant="danger" loading={respondingIds.has(inv.id)} onClick={() => respondMutation.mutate({ id: inv.id, accept: false })}>
                    <X size={12} />
                    Decline
                  </Button>
                </div>
              )}
            </Card>
          ))}
        </div>
      </section>

      <section>
        <SectionHeader title="Sent" icon={<Send size={14} />} count={sent.length || undefined} />

        {sent.length === 0 && !isLoading && (
          <EmptyState
            icon={<Send size={18} />}
            title="No invitations sent"
            description="Send an invitation to another server ID to begin sharing libraries."
            className="py-10"
          />
        )}

        <div className="flex flex-col gap-2">
          {sent.map(inv => (
            <Card key={inv.id} variant="interactive" className="flex flex-col gap-3 sm:flex-row sm:items-center sm:gap-4">
              <div className="flex-1 min-w-0">
                <p className="text-sm text-[var(--color-heading)] font-medium truncate">{inv.toServerName}</p>
                <p className="text-xs text-[var(--color-text)] mt-0.5">{formatDate(inv.createdAt)}</p>
              </div>
              <StatusPill status={inv.status} />
              {inv.status === 'Pending' && (
                <Button size="sm" variant="ghost" loading={revokingIds.has(inv.id)} onClick={() => setInvitationToRevoke(inv)}>
                  <Trash2 size={12} />
                  Revoke
                </Button>
              )}
            </Card>
          ))}
        </div>
      </section>

      <ConfirmDialog
        open={invitationToRevoke !== null}
        title="Revoke invitation?"
        description={invitationToRevoke
          ? <>This will revoke the pending invitation to <span className="text-[var(--color-heading)]">{invitationToRevoke.toServerName}</span>.</>
          : ''}
        confirmLabel="Revoke"
        variant="danger"
        onConfirm={() => {
          if (invitationToRevoke) revokeMutation.mutate(invitationToRevoke.id)
        }}
        onCancel={() => setInvitationToRevoke(null)}
      />
    </div>
  )
}
