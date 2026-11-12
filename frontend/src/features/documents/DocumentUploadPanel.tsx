import { useEffect, useState } from 'react'
import { Loader2, Upload, CheckCircle2, XCircle } from 'lucide-react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { useUploadDocumentMutation, useDocumentStatusQuery } from './documentsApi'

/** Chat sidebar-এর compact upload — .txt/.md → Qdrant ingestion। */
export function DocumentUploadPanel() {
  const [file, setFile] = useState<File | null>(null)
  const [docId, setDocId] = useState<string | null>(null)
  const [isDone, setIsDone] = useState(false)
  const [upload, { isLoading }] = useUploadDocumentMutation()

  const { data: status } = useDocumentStatusQuery(docId ?? '', {
    skip: !docId,
    pollingInterval: isDone ? 0 : 1500,
  })

  useEffect(() => {
    if (status && (status.status === 'Completed' || status.status === 'Failed')) setIsDone(true)
  }, [status])

  const onUpload = async () => {
    if (!file) return
    const formData = new FormData()
    formData.append('file', file)
    try {
      const res = await upload(formData).unwrap()
      setIsDone(false)
      setDocId(res.documentId)
      toast.success('আপলোড হয়েছে — ingestion চলছে')
    } catch {
      toast.error('আপলোড ব্যর্থ হয়েছে')
    }
  }

  const percent =
    status && status.totalChunks > 0
      ? Math.round((status.processedChunks / status.totalChunks) * 100)
      : 0

  return (
    <div className="space-y-2 rounded-2xl border bg-card p-3">
      <p className="text-xs text-muted-foreground">
        .txt / .md → Qdrant। চ্যাটেই agent দিয়ে খুঁজবে।
      </p>
      <input
        type="file"
        accept=".txt,.md"
        onChange={(e) => setFile(e.target.files?.[0] ?? null)}
        className="w-full text-xs file:mr-2 file:rounded-md file:border-0 file:bg-secondary file:px-2 file:py-1 file:text-xs"
      />
      <Button onClick={onUpload} disabled={!file || isLoading} size="sm" className="w-full gap-2">
        {isLoading ? <Loader2 className="size-3.5 animate-spin" /> : <Upload className="size-3.5" />}
        আপলোড
      </Button>
      {status && (
        <div className="space-y-1 text-xs">
          <p className="flex items-center gap-1.5 truncate font-medium">
            {status.status === 'Completed' && <CheckCircle2 className="size-3.5 shrink-0 text-green-600" />}
            {status.status === 'Failed' && <XCircle className="size-3.5 shrink-0 text-destructive" />}
            {!isDone && <Loader2 className="size-3.5 shrink-0 animate-spin text-primary" />}
            <span className="truncate">
              {status.fileName} — {status.status}
            </span>
          </p>
          <div className="h-1.5 w-full overflow-hidden rounded-full bg-muted">
            <div
              className="h-full rounded-full bg-primary transition-all"
              style={{ width: `${status.status === 'Completed' ? 100 : percent}%` }}
            />
          </div>
        </div>
      )}
    </div>
  )
}
