import { useState } from 'react'
import { motion } from 'framer-motion'
import {
  Activity,
  ArrowDownToLine,
  ArrowUpFromLine,
  CheckCircle2,
  DollarSign,
  Loader2,
  PlayCircle,
  XCircle,
} from 'lucide-react'
import { toast } from 'sonner'
import { cn } from '@/lib/utils'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { useAppSelector } from '@/app/hooks'
import { selectDefaultProvider } from '@/features/settings/uiSlice'
import { useRunEvalsMutation, useUsageSummaryQuery } from './dashboardApi'
import { UsageCharts } from './UsageCharts'

import { PROVIDERS } from '@/lib/providers'

const KPI_GRADIENTS = [
  'from-[var(--chart-1)] to-[var(--chart-3)]',
  'from-[var(--chart-3)] to-[var(--chart-2)]',
  'from-[var(--chart-2)] to-[var(--chart-1)]',
  'from-[var(--chart-4)] to-[var(--chart-1)]',
]

function Kpi({
  label,
  value,
  icon: Icon,
  gradient,
  index,
}: {
  label: string
  value: string
  icon: typeof DollarSign
  gradient: string
  index: number
}) {
  return (
    <motion.div
      initial={{ opacity: 0, y: 12 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ delay: index * 0.05, duration: 0.3, ease: 'easeOut' }}
    >
      <Card className="gap-3 rounded-2xl py-5">
        <CardHeader className="pb-0">
          <div className="flex items-center gap-3">
            <span className={cn('flex size-9 items-center justify-center rounded-xl bg-gradient-to-br', gradient)}>
              <Icon className="size-4 text-white" />
            </span>
            <CardTitle className="text-sm font-medium text-muted-foreground">{label}</CardTitle>
          </div>
        </CardHeader>
        <CardContent>
          <p className="text-2xl font-bold tabular-nums">{value}</p>
        </CardContent>
      </Card>
    </motion.div>
  )
}

// skeleton — usage data আসা পর্যন্ত KPI + chart block-এর জায়গা ধরে রাখে (layout shift না)।
function UsageSkeleton() {
  return (
    <div className="space-y-4">
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        {[0, 1, 2, 3].map((i) => (
          <Skeleton key={i} className="h-28" />
        ))}
      </div>
      <div className="grid gap-4 lg:grid-cols-2">
        <Skeleton className="h-56" />
        <Skeleton className="h-56" />
      </div>
      <Skeleton className="h-40" />
    </div>
  )
}

export function DashboardPage() {
  const { data: usage, isLoading: usageLoading } = useUsageSummaryQuery()
  const [provider, setProvider] = useState(useAppSelector(selectDefaultProvider))
  const [runEvals, { data: report, isLoading: evalsLoading }] = useRunEvalsMutation()

  const onRunEvals = async () => {
    try {
      await runEvals(provider).unwrap()
    } catch {
      toast.error('Evals চালানো যায়নি')
    }
  }

  return (
    <div className="mx-auto max-w-6xl space-y-8">
      <div>
        <h1 className="text-3xl font-bold">Dashboard</h1>
        <p className="mt-1 text-muted-foreground">LLM ব্যবহার, খরচ ও RAG evals</p>
      </div>

      {/* --- LLM usage / cost --- */}
      <section className="space-y-4">
        {usageLoading && <UsageSkeleton />}
        {usage && (
          <>
            <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
              <Kpi index={0} label="মোট খরচ" value={`$${usage.totalCostUsd.toFixed(4)}`} icon={DollarSign} gradient={KPI_GRADIENTS[0]} />
              <Kpi index={1} label="মোট request" value={usage.totalRequests.toLocaleString()} icon={Activity} gradient={KPI_GRADIENTS[1]} />
              <Kpi index={2} label="Input tokens" value={usage.totalInputTokens.toLocaleString()} icon={ArrowDownToLine} gradient={KPI_GRADIENTS[2]} />
              <Kpi index={3} label="Output tokens" value={usage.totalOutputTokens.toLocaleString()} icon={ArrowUpFromLine} gradient={KPI_GRADIENTS[3]} />
            </div>

            <motion.div
              initial={{ opacity: 0, y: 12 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ delay: 0.2, duration: 0.3, ease: 'easeOut' }}
              className="space-y-4"
            >
              <UsageCharts byModel={usage.byModel} totalCostUsd={usage.totalCostUsd} />

              <div className="overflow-x-auto rounded-2xl border bg-card backdrop-blur-xl">
                <table className="w-full text-sm">
                  <thead className="border-b text-left text-muted-foreground">
                    <tr>
                      <th className="px-4 py-3 font-medium">Provider</th>
                      <th className="px-4 py-3 font-medium">Model</th>
                      <th className="px-4 py-3 text-right font-medium">Requests</th>
                      <th className="px-4 py-3 text-right font-medium">Input</th>
                      <th className="px-4 py-3 text-right font-medium">Output</th>
                      <th className="px-4 py-3 text-right font-medium">Cost</th>
                    </tr>
                  </thead>
                  <tbody>
                    {usage.byModel.length === 0 && (
                      <tr>
                        <td colSpan={6} className="px-4 py-6 text-center text-muted-foreground">
                          এখনো কোনো LLM call হয়নি।
                        </td>
                      </tr>
                    )}
                    {usage.byModel.map((m) => (
                      <tr key={`${m.provider}-${m.model}`} className="border-b transition-colors last:border-0 hover:bg-accent/50">
                        <td className="px-4 py-3">{m.provider}</td>
                        <td className="px-4 py-3">{m.model}</td>
                        <td className="px-4 py-3 text-right tabular-nums">{m.requests.toLocaleString()}</td>
                        <td className="px-4 py-3 text-right tabular-nums">{m.inputTokens.toLocaleString()}</td>
                        <td className="px-4 py-3 text-right tabular-nums">{m.outputTokens.toLocaleString()}</td>
                        <td className="px-4 py-3 text-right tabular-nums">${m.costUsd.toFixed(4)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </motion.div>
          </>
        )}
      </section>

      {/* --- Evals --- */}
      <section className="space-y-4">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h2 className="text-lg font-semibold">RAG Evals</h2>
          <div className="flex items-center gap-2">
            <Select value={provider} onValueChange={setProvider}>
              <SelectTrigger className="w-32 rounded-xl">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {PROVIDERS.map((p) => (
                  <SelectItem key={p} value={p}>
                    {p}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            <Button onClick={onRunEvals} disabled={evalsLoading} className="gap-2 rounded-xl">
              {evalsLoading ? <Loader2 className="size-4 animate-spin" /> : <PlayCircle className="size-4" />}
              Run evals
            </Button>
          </div>
        </div>

        {evalsLoading && (
          <div className="space-y-2">
            {[0, 1, 2].map((i) => (
              <Skeleton key={i} className="h-16" />
            ))}
          </div>
        )}

        {report && !evalsLoading && (
          <div className="space-y-3">
            <p className="text-sm">
              Pass rate: <span className="font-bold">{Math.round(report.passRate * 100)}%</span>{' '}
              <span className="text-muted-foreground">
                ({report.passed}/{report.total})
              </span>
            </p>
            <div className="space-y-2">
              {report.results.map((r, i) => (
                <motion.div
                  key={r.id}
                  initial={{ opacity: 0, y: 8 }}
                  animate={{ opacity: 1, y: 0 }}
                  transition={{ delay: i * 0.04, duration: 0.25, ease: 'easeOut' }}
                  className="flex items-start gap-2 rounded-2xl border bg-card p-3 text-sm backdrop-blur-xl"
                >
                  {r.passed ? (
                    <CheckCircle2 className="mt-0.5 size-4 shrink-0 text-green-600" />
                  ) : (
                    <XCircle className="mt-0.5 size-4 shrink-0 text-destructive" />
                  )}
                  <div className="min-w-0">
                    <p className="font-medium">{r.id}</p>
                    <p className={cn('truncate text-muted-foreground', !r.passed && 'text-destructive')}>
                      {r.passed ? r.answer : r.failReason}
                    </p>
                  </div>
                </motion.div>
              ))}
            </div>
          </div>
        )}
      </section>
    </div>
  )
}
