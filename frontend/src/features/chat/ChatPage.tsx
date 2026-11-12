import { useEffect, useRef, useState } from 'react'
import { Loader2, Plus, SendHorizontal } from 'lucide-react'
import { toast } from 'sonner'
import { cn } from '@/lib/utils'
import { TOOL_PROVIDERS, isOllama, effectiveProvider } from '@/lib/providers'
import { apiErrorMessage } from '@/lib/apiError'
import { useAppSelector } from '@/app/hooks'
import { selectDefaultProvider } from '@/features/settings/uiSlice'
import { DocumentUploadPanel } from '@/features/documents/DocumentUploadPanel'
import { Button } from '@/components/ui/button'
import { Textarea } from '@/components/ui/textarea'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { Switch } from '@/components/ui/switch'
import { Label } from '@/components/ui/label'
import { Timing } from '@/components/Timing'
import { useLocalModelsQuery } from './modelsApi'
import { useAgentMutation, type ScoredHit } from './agentApi'
import { useLazyGetConversationQuery, useListConversationsQuery } from './conversationsApi'

interface Message {
  role: 'user' | 'assistant'
  content: string
  totalMs?: number
  llmMs?: number
  toolsUsed?: string[]
  toolHits?: ScoredHit[]
  documentHits?: ScoredHit[]
}

/** এক জায়গায় প্রশ্ন — Agent টুল (ERP + uploaded docs) একসাথে চালিয়ে উত্তর দেয়। */
export function ChatPage() {
  const defaultProvider = useAppSelector(selectDefaultProvider)
  const [messages, setMessages] = useState<Message[]>([])
  const [input, setInput] = useState('')
  const [provider, setProvider] = useState(
    TOOL_PROVIDERS.includes(defaultProvider) ? defaultProvider : TOOL_PROVIDERS[0],
  )
  const [model, setModel] = useState<string | undefined>(undefined)
  const [conversationId, setConversationId] = useState<string | null>(null)
  const [ollamaOnServer, setOllamaOnServer] = useState(false)
  const [agent, { isLoading: busy }] = useAgentMutation()
  const bottomRef = useRef<HTMLDivElement>(null)

  const sentProvider = effectiveProvider(provider, ollamaOnServer)
  const isLocal = isOllama(provider)
  const { currentData: localModels } = useLocalModelsQuery(sentProvider, { skip: !isLocal })

  useEffect(() => {
    if (isLocal && localModels?.length) setModel((m) => (m && localModels.includes(m) ? m : localModels[0]))
    else setModel(undefined)
  }, [isLocal, localModels])

  const { data: conversations } = useListConversationsQuery()
  const [fetchConversation] = useLazyGetConversationQuery()

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [messages])

  const newChat = () => {
    if (busy) return
    setConversationId(null)
    setMessages([])
  }

  const openConversation = async (id: string) => {
    if (busy) return
    setConversationId(id)
    try {
      const detail = await fetchConversation(id).unwrap()
      setMessages(
        detail.messages
          .filter((m) => m.role !== 'system')
          .map((m) => ({ role: m.role as 'user' | 'assistant', content: m.content })),
      )
    } catch {
      toast.error('কথোপকথন লোড করা যায়নি')
    }
  }

  const send = async () => {
    const text = input.trim()
    if (!text || busy) return

    setInput('')
    setMessages((m) => [...m, { role: 'user', content: text }, { role: 'assistant', content: '' }])

    const started = performance.now()
    try {
      const result = await agent({
        message: text,
        provider: sentProvider,
        model: isLocal ? model : undefined,
      }).unwrap()
      setMessages((m) => {
        const copy = [...m]
        copy[copy.length - 1] = {
          role: 'assistant',
          content: result.answer,
          totalMs: performance.now() - started,
          llmMs: result.latencyMs,
          toolsUsed: result.toolsUsed,
          toolHits: result.toolHits ?? undefined,
          documentHits: result.documentHits ?? undefined,
        }
        return copy
      })
      if (!conversationId) setConversationId(crypto.randomUUID())
    } catch (err) {
      const reason = apiErrorMessage(err)
      toast.error(reason)
      setMessages((m) => {
        const copy = [...m]
        copy[copy.length - 1] = { role: 'assistant', content: reason }
        return copy
      })
    }
  }

  const onKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault()
      void send()
    }
  }

  return (
    <div className="mx-auto flex h-[calc(100vh-8rem)] max-w-6xl gap-4">
      <aside className="hidden w-64 shrink-0 flex-col gap-3 sm:flex">
        <Button onClick={newChat} variant="outline" className="justify-start gap-2">
          <Plus className="size-4" /> নতুন চ্যাট
        </Button>
        <div className="min-h-0 flex-1 space-y-1 overflow-y-auto rounded-2xl border bg-card p-2">
          {conversations?.length === 0 && (
            <p className="px-3 py-2 text-xs text-muted-foreground">এখনো কোনো চ্যাট নেই</p>
          )}
          {conversations?.map((c) => (
            <button
              key={c.conversationId}
              onClick={() => openConversation(c.conversationId)}
              className={cn(
                'w-full truncate rounded-lg px-3 py-2 text-left text-sm transition-colors',
                c.conversationId === conversationId ? 'bg-muted font-medium' : 'hover:bg-muted/60',
              )}
              title={c.title}
            >
              {c.title}
            </button>
          ))}
        </div>
        <div className="shrink-0">
          <p className="mb-2 px-1 text-xs font-medium text-muted-foreground">Documents</p>
          <DocumentUploadPanel />
        </div>
      </aside>

      <div className="flex flex-1 flex-col">
        <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
          <div>
            <h1 className="text-xl font-bold">Chat</h1>
            <p className="text-xs text-muted-foreground">
              এক বাক্স — ডক/পলিসি + ERP টুল একসাথে; নিচে toolsUsed + vector scores
            </p>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <Select value={provider} onValueChange={setProvider}>
              <SelectTrigger className="w-36">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {TOOL_PROVIDERS.map((p) => (
                  <SelectItem key={p} value={p}>
                    {p}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>

            {isLocal && (
              <div className="flex items-center gap-2">
                <Switch
                  id="ollama-server"
                  checked={ollamaOnServer}
                  onCheckedChange={setOllamaOnServer}
                  disabled={busy}
                />
                <Label htmlFor="ollama-server" className="cursor-pointer whitespace-nowrap text-sm">
                  {ollamaOnServer ? 'GPU সার্ভার' : 'এই মেশিন'}
                </Label>
              </div>
            )}

            {isLocal && localModels && localModels.length > 0 && (
              <Select value={model} onValueChange={setModel}>
                <SelectTrigger className="w-40">
                  <SelectValue placeholder="model" />
                </SelectTrigger>
                <SelectContent>
                  {localModels.map((mo) => (
                    <SelectItem key={mo} value={mo}>
                      {mo}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            )}
          </div>
        </div>

        <div className="mb-3 sm:hidden">
          <DocumentUploadPanel />
        </div>

        <div className="flex-1 space-y-4 overflow-y-auto rounded-2xl border bg-card p-4">
          {messages.length === 0 && (
            <p className="mt-10 text-center text-sm text-muted-foreground">
              যা খুশি জিজ্ঞাসা করো — বিক্রি/স্টক হলে ERP টুল, ডক/নীতি হলে document search; দুটো মিলিয়েও
              উত্তর দিতে পারে।
            </p>
          )}
          {messages.map((msg, i) => (
            <div key={i} className={cn('flex', msg.role === 'user' ? 'justify-end' : 'justify-start')}>
              <div className="max-w-[80%]">
                <div
                  className={cn(
                    'whitespace-pre-wrap rounded-2xl px-4 py-2 text-sm',
                    msg.role === 'user'
                      ? 'bg-primary text-primary-foreground'
                      : 'bg-muted text-foreground',
                  )}
                >
                  {msg.content || (busy && i === messages.length - 1 ? '…' : '')}
                </div>

                {msg.toolsUsed && msg.toolsUsed.length > 0 && (
                  <div className="mt-1 flex flex-wrap gap-1">
                    {msg.toolsUsed.map((tool, t) => (
                      <span
                        key={t}
                        className="rounded bg-primary/10 px-1.5 py-0.5 text-xs font-medium text-primary"
                      >
                        {tool}
                      </span>
                    ))}
                  </div>
                )}

                {(msg.toolHits?.length || msg.documentHits?.length) ? (
                  <div className="mt-2 space-y-2 text-[11px] text-muted-foreground">
                    {msg.toolHits && msg.toolHits.length > 0 && (
                      <ScoreList title="Tools collection (top 8)" hits={msg.toolHits} />
                    )}
                    {msg.documentHits && msg.documentHits.length > 0 && (
                      <ScoreList title="Docs collection (top 8)" hits={msg.documentHits} />
                    )}
                  </div>
                ) : null}

                {msg.totalMs != null && <Timing totalMs={msg.totalMs} parts={{ LLM: msg.llmMs ?? 0 }} />}
              </div>
            </div>
          ))}
          <div ref={bottomRef} />
        </div>

        <div className="mt-3 flex items-end gap-2">
          <Textarea
            value={input}
            onChange={(e) => setInput(e.target.value)}
            onKeyDown={onKeyDown}
            placeholder="আপনার বার্তা লিখুন... (Enter পাঠাতে)"
            className="min-h-11 resize-none"
            rows={1}
          />
          <Button onClick={send} disabled={busy || !input.trim()} size="icon" className="size-11 shrink-0">
            {busy ? <Loader2 className="size-4 animate-spin" /> : <SendHorizontal className="size-4" />}
          </Button>
        </div>
      </div>
    </div>
  )
}

function ScoreList({ title, hits }: { title: string; hits: ScoredHit[] }) {
  return (
    <div className="rounded-lg border bg-background/50 px-2 py-1.5">
      <p className="mb-1 font-medium text-foreground/80">{title}</p>
      <ol className="space-y-0.5">
        {hits.slice(0, 8).map((h, i) => (
          <li key={i} className="flex gap-2 tabular-nums">
            <span className="w-10 shrink-0 text-right font-medium text-foreground">
              {h.score.toFixed(3)}
            </span>
            <span className="min-w-0 truncate" title={h.detail ? `${h.label} · ${h.detail}` : h.label}>
              {h.label}
              {h.detail ? <span className="ml-1 opacity-60">({h.detail})</span> : null}
            </span>
          </li>
        ))}
      </ol>
    </div>
  )
}
