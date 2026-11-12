import { Loader2, Search } from 'lucide-react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Textarea } from '@/components/ui/textarea'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { useEffect, useState } from 'react'
import { useAppSelector } from '@/app/hooks'
import { selectDefaultProvider } from '@/features/settings/uiSlice'
import { useLocalModelsQuery } from '@/features/chat/modelsApi'
import { Switch } from '@/components/ui/switch'
import { Label } from '@/components/ui/label'
import { Timing } from '@/components/Timing'
import { useRagMutation } from './ragApi'

// keyword / embedding / hybrid তিনটাই seed KnowledgeBase-only — আপলোড করা ডকুমেন্ট দেখে না।
// qdrant একাই দুটোই দেখে, তাই আপাতত বাকিগুলো লুকানো। ফেরানোর নিয়ম docs/hidden-features.md।
// const METHODS = ['keyword', 'embedding', 'qdrant', 'hybrid']
const METHODS = ['qdrant']
import { PROVIDERS, isOllama, effectiveProvider } from '@/lib/providers'
import { apiErrorMessage } from '@/lib/apiError'

export function RagPage() {
  const [question, setQuestion] = useState('')
  const [method, setMethod] = useState(METHODS[0])
  const [provider, setProvider] = useState(useAppSelector(selectDefaultProvider))
  const [model, setModel] = useState<string | undefined>(undefined)
  // ollama এই মেশিনে না GPU সার্ভারে — chat page-এর মতোই toggle
  const [ollamaOnServer, setOllamaOnServer] = useState(false)
  const [rag, { data, isLoading }] = useRagMutation()

  // ollama select করলে installed model তালিকা (chat page-এর একই endpoint); অন্য provider-এ skip
  const isLocal = isOllama(provider)
  const sentProvider = effectiveProvider(provider, ollamaOnServer)
  // ⚠️ currentData, data নয়: RTK Query নতুন argument-এর fetch চলাকালীন `data`-তে আগের
  // argument-এর ফল ধরে রাখে — তখন এই মেশিনের model তালিকা GPU সার্ভারের নামে দেখাত।
  const { currentData: localModels } = useLocalModelsQuery(sentProvider, { skip: !isLocal })
  useEffect(() => {
    if (isLocal && localModels?.length) setModel((m) => (m && localModels.includes(m) ? m : localModels[0]))
    else setModel(undefined)
  }, [isLocal, localModels])

  // request→response মোট সময় (network+queue সহ) — backend-এর খাঁটি সময়ের পাশে দেখাই।
  const [totalMs, setTotalMs] = useState(0)

  const ask = async () => {
    const q = question.trim()
    if (!q) return
    const started = performance.now()
    try {
      await rag({ question: q, method, provider: sentProvider, model }).unwrap()
      setTotalMs(performance.now() - started)
    } catch (err) {
      // আসল কারণ দেখাই (যেমন retired model-এ 502) — generic বার্তায় মনে হতো আমাদের bug।
      toast.error(apiErrorMessage(err))
    }
  }

  return (
    <div className="mx-auto max-w-3xl space-y-4">
      <h1 className="text-xl font-bold">RAG</h1>

      <div className="flex flex-wrap gap-2">
        <Select value={method} onValueChange={setMethod}>
          <SelectTrigger className="w-40"><SelectValue /></SelectTrigger>
          <SelectContent>
            {METHODS.map((m) => <SelectItem key={m} value={m}>{m}</SelectItem>)}
          </SelectContent>
        </Select>
        <Select value={provider} onValueChange={setProvider}>
          <SelectTrigger className="w-36"><SelectValue /></SelectTrigger>
          <SelectContent>
            {PROVIDERS.map((p) => <SelectItem key={p} value={p}>{p}</SelectItem>)}
          </SelectContent>
        </Select>

        {/* ollama কোথায় চলবে — এই মেশিন না GPU সার্ভার (chat page-এর মতোই) */}
        {isLocal && (
          <div className="flex items-center gap-2">
            <Switch
              id="rag-ollama-server"
              checked={ollamaOnServer}
              onCheckedChange={setOllamaOnServer}
              disabled={isLoading}
            />
            <Label htmlFor="rag-ollama-server" className="cursor-pointer whitespace-nowrap text-sm">
              {ollamaOnServer ? 'GPU সার্ভার' : 'এই মেশিন'}
            </Label>
          </div>
        )}

        {/* ollama select করলে installed model-এর 2nd dropdown (chat page-এর মতোই) */}
        {isLocal && localModels && localModels.length > 0 && (
          <Select value={model} onValueChange={setModel}>
            <SelectTrigger className="w-40"><SelectValue placeholder="model" /></SelectTrigger>
            <SelectContent>
              {localModels.map((mo) => <SelectItem key={mo} value={mo}>{mo}</SelectItem>)}
            </SelectContent>
          </Select>
        )}

        {/* তালিকা খালি = ওই host-এ পৌঁছানো যায়নি (catalog খালি ফেরত দেয়, throw করে না) */}
        {isLocal && localModels?.length === 0 && (
          <span className="self-center text-sm text-destructive">
            {ollamaOnServer ? 'GPU সার্ভারে পৌঁছানো যাচ্ছে না' : 'এই মেশিনে ollama চলছে না'}
          </span>
        )}
      </div>

      <Textarea
        value={question}
        onChange={(e) => setQuestion(e.target.value)}
        placeholder="আপনার প্রশ্ন লিখুন... (knowledge base + আপলোড করা ডকুমেন্ট থেকে উত্তর)"
        rows={3}
      />
      <Button onClick={ask} disabled={isLoading || !question.trim()} className="gap-2">
        {isLoading ? <Loader2 className="size-4 animate-spin" /> : <Search className="size-4" />}
        জিজ্ঞাসা করুন
      </Button>

      {data && (
        <div className="space-y-4">
          <Card>
            <CardHeader><CardTitle className="text-base">উত্তর</CardTitle></CardHeader>
            <CardContent className="text-sm">
              <div className="whitespace-pre-wrap">{data.answer}</div>
              <Timing
                totalMs={totalMs}
                parts={{ retrieval: data.retrievalMs, generation: data.generationMs }}
              />
            </CardContent>
          </Card>

          {data.sources.length > 0 && (
            <div className="space-y-2">
              <p className="text-sm font-medium text-muted-foreground">সূত্র ({data.sources.length})</p>
              {data.sources.map((s, i) => (
                <div key={i} className="rounded-lg border bg-muted/40 p-3 text-sm">
                  <div className="mb-1 flex items-center gap-2 text-xs text-muted-foreground">
                    <span className="rounded bg-primary/10 px-1.5 py-0.5 font-medium text-primary">[{i + 1}]</span>
                    {s.source && <span className="rounded bg-secondary px-1.5 py-0.5">{s.source}</span>}
                    {s.score != null && <span>score: {s.score.toFixed(3)}</span>}
                  </div>
                  {s.text}
                </div>
              ))}
            </div>
          )}
        </div>
      )}
    </div>
  )
}
