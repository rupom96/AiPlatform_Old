import { useEffect, useState } from 'react'
import { Loader2, Upload, CheckCircle2, XCircle, Trash2 } from 'lucide-react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { apiErrorMessage } from '@/lib/apiError'
import { cn } from '@/lib/utils'
import {
  useUploadDocumentMutation,
  useDocumentStatusQuery,
  useListDocumentsQuery,
  useDeleteDocumentMutation,
} from './documentsApi'

// ingestion চলার সময় তালিকা কত ঘন ঘন নতুন করে আনা হবে (ms); কিছু চলছে না হলে polling নেই।
const LIST_POLL_MS = 2000

/** Chat sidebar-এর compact upload + আপলোড করা ডকুমেন্টের তালিকা ও মোছা — .txt/.md → Qdrant ingestion। */
export function DocumentUploadPanel() {
  const [file, setFile] = useState<File | null>(null)
  const [docId, setDocId] = useState<string | null>(null)
  const [isDone, setIsDone] = useState(false)
  const [upload, { isLoading }] = useUploadDocumentMutation()

  const [listPolling, setListPolling] = useState(0)
  const { data: documents } = useListDocumentsQuery(undefined, { pollingInterval: listPolling })
  const [deleteDocument, { isLoading: isDeleting }] = useDeleteDocumentMutation()
  // দুই ধাপে মোছা: প্রথম ক্লিকে "মুছবেন?" আসে — dialog ছাড়াই ভুল-ক্লিকে মোছা আটকায়।
  const [confirmId, setConfirmId] = useState<string | null>(null)

  const { data: status } = useDocumentStatusQuery(docId ?? '', {
    skip: !docId,
    pollingInterval: isDone ? 0 : 1500,
  })

  useEffect(() => {
    if (status && (status.status === 'Completed' || status.status === 'Failed')) setIsDone(true)
  }, [status])

  // কোনো ডকুমেন্ট Pending/Processing থাকলে তালিকা নিজে থেকে হালনাগাদ হয় (শেষ হলে থামে)
  useEffect(() => {
    const active = documents?.items.some((d) => d.status === 'Pending' || d.status === 'Processing')
    setListPolling(active ? LIST_POLL_MS : 0)
  }, [documents])

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

  const onDelete = async (id: string, fileName: string) => {
    try {
      await deleteDocument(id).unwrap()
      if (docId === id) setDocId(null) // উপরের progress অংশও সরাও
      toast.success(`${fileName} মুছে ফেলা হয়েছে`)
    } catch (err) {
      toast.error(apiErrorMessage(err, 'মোছা যায়নি'))
    } finally {
      setConfirmId(null)
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
          {/* ব্যর্থ হলে কারণ (যেমন embedding provider-এর error) — আগে শুধু লাল ✖ দেখাত, কারণ বোঝা যেত না */}
          {status.error && (
            <p className="line-clamp-3 break-words text-destructive" title={status.error}>
              {status.error}
            </p>
          )}
        </div>
      )}

      {documents && documents.items.length > 0 && (
        <div className="space-y-1.5 border-t pt-2">
          <p className="text-xs font-medium text-muted-foreground">
            আপলোড করা ডকুমেন্ট ({documents.totalCount})
          </p>
          <ul className="max-h-52 space-y-1.5 overflow-y-auto">
            {documents.items.map((doc) => (
              <li key={doc.id} className="flex items-center justify-between gap-2 rounded-lg bg-muted/40 px-2 py-1.5 text-xs">
                <div className="min-w-0">
                  <p className="truncate font-medium" title={doc.fileName}>
                    {doc.fileName}
                  </p>
                  <p
                    className={cn('truncate text-[11px] text-muted-foreground', doc.status === 'Failed' && 'text-destructive')}
                    title={doc.error ?? undefined}
                  >
                    {doc.status}
                    {doc.totalChunks > 0 && ` — ${doc.totalChunks} chunk`}
                  </p>
                </div>

                {confirmId === doc.id ? (
                  <div className="flex shrink-0 items-center gap-1">
                    <Button
                      size="sm"
                      variant="destructive"
                      className="h-6 px-2 text-[11px]"
                      disabled={isDeleting}
                      onClick={() => onDelete(doc.id, doc.fileName)}
                    >
                      {isDeleting ? <Loader2 className="size-3 animate-spin" /> : 'মুছুন'}
                    </Button>
                    <Button size="sm" variant="ghost" className="h-6 px-2 text-[11px]" onClick={() => setConfirmId(null)}>
                      বাতিল
                    </Button>
                  </div>
                ) : (
                  <Button
                    size="icon"
                    variant="ghost"
                    className="size-6 shrink-0 text-muted-foreground hover:text-destructive"
                    aria-label={`${doc.fileName} মুছুন`}
                    onClick={() => setConfirmId(doc.id)}
                  >
                    <Trash2 className="size-3.5" />
                  </Button>
                )}
              </li>
            ))}
          </ul>
          <p className="text-[11px] text-muted-foreground">
            মুছলে ডকুমেন্ট ও তার সব chunk (Qdrant-সহ) যাবে, ফেরানো যাবে না।
          </p>
        </div>
      )}
    </div>
  )
}
