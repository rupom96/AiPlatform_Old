import { Bar, BarChart, CartesianGrid, Cell, Legend, Pie, PieChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import type { UsageByModel } from './dashboardApi'

// series color follows entity (model) — fixed slot order, কখনো cycle না; ৪-এর বেশি model হলে "Others"-এ fold।
const SLOTS: string[] = ['var(--chart-1)', 'var(--chart-2)', 'var(--chart-3)', 'var(--chart-4)']
const OTHERS_COLOR = 'var(--muted-foreground)'

// isAnimationActive={false} সব জায়গায় — entry animation বাদ: data ছোট, চার্ট সাথে সাথে আঁকা হয়,
// আর rAF-throttled environment-এ (background tab, headless) animation-pending অবস্থায় আটকে থাকে না।
const tooltipStyle = {
  backgroundColor: 'var(--popover)',
  border: '1px solid var(--border)',
  borderRadius: 12,
  color: 'var(--popover-foreground)',
  fontSize: 12,
}
const itemStyle = { color: 'var(--popover-foreground)' }
const axisTick = { fill: 'var(--muted-foreground)', fontSize: 12 }

interface Slice {
  name: string
  provider: string
  costUsd: number
  inputTokens: number
  outputTokens: number
  color: string
}

// top-4 model + বাকিরা "Others" — slot রং identity ধরে রাখে।
function toSlices(byModel: UsageByModel[]): Slice[] {
  const sorted = [...byModel].sort((a, b) => b.costUsd - a.costUsd)
  const top = sorted.slice(0, 4).map((m, i) => ({
    name: m.model,
    provider: m.provider,
    costUsd: m.costUsd,
    inputTokens: m.inputTokens,
    outputTokens: m.outputTokens,
    color: SLOTS[i],
  }))
  const rest = sorted.slice(4)
  if (rest.length > 0) {
    top.push({
      name: 'Others',
      provider: `${rest.length} models`,
      costUsd: rest.reduce((s, m) => s + m.costUsd, 0),
      inputTokens: rest.reduce((s, m) => s + m.inputTokens, 0),
      outputTokens: rest.reduce((s, m) => s + m.outputTokens, 0),
      color: OTHERS_COLOR,
    })
  }
  return top
}

function ChartCard({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <Card className="rounded-2xl">
      <CardHeader className="pb-2">
        <CardTitle className="text-sm font-medium text-muted-foreground">{title}</CardTitle>
      </CardHeader>
      <CardContent>{children}</CardContent>
    </Card>
  )
}

// donut — model-প্রতি খরচের ভাগ; পাশে legend list (direct label = রঙের relief)।
function CostDonut({ slices, totalCostUsd }: { slices: Slice[]; totalCostUsd: number }) {
  return (
    <ChartCard title="খরচের ভাগ (model-প্রতি)">
      <div className="flex flex-wrap items-center gap-6">
        <div className="relative">
          <PieChart width={180} height={180}>
            <Pie
              data={slices}
              dataKey="costUsd"
              nameKey="name"
              innerRadius={58}
              outerRadius={82}
              paddingAngle={3}
              strokeWidth={0}
              isAnimationActive={false}
            >
              {slices.map((s) => (
                <Cell key={s.name} fill={s.color} />
              ))}
            </Pie>
            <Tooltip
              contentStyle={tooltipStyle}
              itemStyle={itemStyle}
              formatter={(v) => [`$${Number(v).toFixed(4)}`, 'খরচ']}
            />
          </PieChart>
          <div className="pointer-events-none absolute inset-0 flex flex-col items-center justify-center">
            <span className="text-xs text-muted-foreground">মোট</span>
            <span className="text-lg font-bold">${totalCostUsd.toFixed(4)}</span>
          </div>
        </div>
        <ul className="min-w-0 flex-1 space-y-2 text-sm">
          {slices.map((s) => (
            <li key={s.name} className="flex items-center gap-2">
              <span className="size-2.5 shrink-0 rounded-full" style={{ backgroundColor: s.color }} />
              <span className="min-w-0 flex-1 truncate" title={`${s.provider} / ${s.name}`}>
                {s.name}
              </span>
              <span className="font-medium tabular-nums">${s.costUsd.toFixed(4)}</span>
            </li>
          ))}
        </ul>
      </div>
    </ChartCard>
  )
}

// horizontal grouped bar — model-প্রতি input/output tokens।
function TokensBar({ slices }: { slices: Slice[] }) {
  return (
    <ChartCard title="Tokens (model-প্রতি)">
      <ResponsiveContainer width="100%" height={slices.length * 56 + 64}>
        <BarChart data={slices} layout="vertical" margin={{ left: 8, right: 24 }}>
          <CartesianGrid horizontal={false} stroke="var(--border)" />
          <XAxis
            type="number"
            tick={axisTick}
            tickFormatter={(v: number) => v.toLocaleString()}
            axisLine={{ stroke: 'var(--border)' }}
            tickLine={false}
          />
          <YAxis type="category" dataKey="name" width={150} tick={axisTick} axisLine={false} tickLine={false} />
          <Tooltip
            cursor={{ fill: 'var(--accent)' }}
            contentStyle={tooltipStyle}
            itemStyle={itemStyle}
            formatter={(v, key) => [Number(v).toLocaleString(), key === 'inputTokens' ? 'Input' : 'Output']}
            labelFormatter={(label, payload) => `${payload?.[0]?.payload.provider} / ${label}`}
          />
          <Legend
            formatter={(value: string) => (
              <span style={{ color: 'var(--muted-foreground)', fontSize: 12 }}>
                {value === 'inputTokens' ? 'Input' : 'Output'}
              </span>
            )}
          />
          <Bar dataKey="inputTokens" fill="var(--chart-1)" barSize={10} radius={[0, 4, 4, 0]} isAnimationActive={false} />
          <Bar dataKey="outputTokens" fill="var(--chart-2)" barSize={10} radius={[0, 4, 4, 0]} isAnimationActive={false} />
        </BarChart>
      </ResponsiveContainer>
    </ChartCard>
  )
}

// dashboard charts — উৎস: GET /usage byModel। table view নিচে থাকে (data + accessibility relief)।
export function UsageCharts({ byModel, totalCostUsd }: { byModel: UsageByModel[]; totalCostUsd: number }) {
  if (byModel.length === 0) return null
  const slices = toSlices(byModel)
  return (
    <div className="grid gap-4 lg:grid-cols-2">
      <CostDonut slices={slices} totalCostUsd={totalCostUsd} />
      <TokensBar slices={slices} />
    </div>
  )
}
