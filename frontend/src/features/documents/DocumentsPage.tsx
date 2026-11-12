import { useEffect, useState } from 'react'
import { Loader2, Upload, CheckCircle2, XCircle, Trash2 } from 'lucide-react'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { apiErrorMessage } from '@/lib/apiError'
import {
  useUploadDocumentMutation,
  useDocumentStatusQuery,
  useListDocumentsQuery,
  useDeleteDocumentMutation,
} from './documentsApi'

export function DocumentsPage() {
  const [file, setFile] = useState<File | null>(null)
  const [docId, setDocId] = useState<string | null>(null)
  const [isDone, setIsDone] = useState(false)
  const [upload, { isLoading }] = useUploadDocumentMutation()

  const { data: documents, isLoading: isListLoading } = useListDocumentsQuery()
  const [deleteDocument, { isLoading: isDeleting }] = useDeleteDocumentMutation()
  // দুই ধাপে মোছা: প্রথম ক্লিকে বোতামটাই "নিশ্চিত?" হয়ে যায়।
  // ponytail: এতে dialog component ছাড়াই ভুল-ক্লিকে মোছা আটকায়; সত্যিকারের modal লাগলে পরে।
  const [confirmId, setConfirmId] = useState<string | null>(null)

  // Completed/Failed হলে polling থামাও (pollingInterval 0)
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

  const onDelete = async (id: string, fileName: string) => {
    try {
      await deleteDocument(id).unwrap()
      if (docId === id) setDocId(null)   // উপরের progress card-ও সরাও
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
    <div className="mx-auto max-w-2xl space-y-4">
      <h1 className="text-xl font-bold">Documents</h1>
      <p className="text-sm text-muted-foreground">
        .txt / .md আপলোড করুন → background-এ chunk → embed → Qdrant। শেষ হলে RAG-এ (method: qdrant) এই ডকুমেন্ট থেকে উত্তর পাবেন।
      </p>

      <Card>
        <CardContent className="flex flex-wrap items-center gap-3 pt-6">
          <input
            type="file"
            accept=".txt,.md"
            onChange={(e) => setFile(e.target.files?.[0] ?? null)}
            className="text-sm file:mr-3 file:rounded-md file:border-0 file:bg-secondary file:px-3 file:py-1.5 file:text-sm"
          />
          <Button onClick={onUpload} disabled={!file || isLoading} className="gap-2">
            {isLoading ? <Loader2 className="size-4 animate-spin" /> : <Upload className="size-4" />}
            আপলোড
          </Button>
        </CardContent>
      </Card>

      {status && (
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2 text-base">
              {status.status === 'Completed' && <CheckCircle2 className="size-5 text-green-600" />}
              {status.status === 'Failed' && <XCircle className="size-5 text-destructive" />}
              {!isDone && <Loader2 className="size-5 animate-spin text-primary" />}
              {status.fileName} — {status.status}
            </CardTitle>
          </CardHeader>
          <CardContent className="space-y-2">
            <div className="h-2 w-full overflow-hidden rounded-full bg-muted">
              <div
                className="h-full rounded-full bg-primary transition-all"
                style={{ width: `${status.status === 'Completed' ? 100 : percent}%` }}
              />
            </div>
            <p className="text-xs text-muted-foreground">
              {status.processedChunks}/{status.totalChunks} chunk
              {status.error && <span className="text-destructive"> — {status.error}</span>}
            </p>
          </CardContent>
        </Card>
      )}

      <Card>
        <CardHeader>
          <CardTitle className="text-base">
            আপলোড করা ডকুমেন্ট {documents && `(${documents.totalCount})`}
          </CardTitle>
        </CardHeader>
        <CardContent className="space-y-2">
          {isListLoading && (
            <>
              <Skeleton className="h-12 w-full" />
              <Skeleton className="h-12 w-full" />
            </>
          )}

          {documents?.items.length === 0 && (
            <p className="text-sm text-muted-foreground">এখনো কোনো ডকুমেন্ট নেই।</p>
          )}

          {documents?.items.map((doc) => (
            <div
              key={doc.id}
              className="flex items-center justify-between gap-3 rounded-lg border bg-muted/40 p-3"
            >
              <div className="min-w-0">
                <p className="truncate text-sm font-medium">{doc.fileName}</p>
                <p className="text-xs text-muted-foreground">
                  {doc.status} — {doc.totalChunks} chunk
                </p>
              </div>

              {confirmId === doc.id ? (
                <div className="flex shrink-0 items-center gap-2">
                  <span className="text-xs text-muted-foreground">নিশ্চিত?</span>
                  <Button
                    size="sm"
                    variant="destructive"
                    disabled={isDeleting}
                    onClick={() => onDelete(doc.id, doc.fileName)}
                  >
                    {isDeleting ? <Loader2 className="size-4 animate-spin" /> : 'মুছুন'}
                  </Button>
                  <Button size="sm" variant="ghost" onClick={() => setConfirmId(null)}>
                    বাতিল
                  </Button>
                </div>
              ) : (
                <Button
                  size="icon"
                  variant="ghost"
                  className="shrink-0 text-muted-foreground hover:text-destructive"
                  aria-label={`${doc.fileName} মুছুন`}
                  onClick={() => setConfirmId(doc.id)}
                >
                  <Trash2 className="size-4" />
                </Button>
              )}
            </div>
          ))}

          <p className="pt-1 text-xs text-muted-foreground">
            মুছলে ডকুমেন্ট ও তার সব chunk দুটোই যাবে — তালিকা থেকে এবং Qdrant থেকে। ফেরানো যাবে না।
          </p>
        </CardContent>
      </Card>
    </div>
  )
}
