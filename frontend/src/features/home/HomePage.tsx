import { Link } from 'react-router-dom'
import { motion } from 'framer-motion'
// BarChart3 hidden সাথে — docs/hidden-features.md
import { MessageSquare, Settings } from 'lucide-react'
import { Card, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { useMeQuery } from '@/features/auth/authApi'
import { useT } from '@/i18n/useT'

const features = [
  {
    to: '/chat',
    icon: MessageSquare,
    title: 'Chat',
    desc: 'ডক আপলোড + ERP/policy — এক বাক্সে agent উত্তর',
  },
  // { to: '/dashboard', icon: BarChart3, title: 'Dashboard', desc: 'LLM খরচ/টোকেন + RAG evals' },   // hidden — docs/hidden-features.md
  { to: '/settings', icon: Settings, title: 'Settings', desc: 'ভাষা / থিম / ডিফল্ট প্রোভাইডার' },
]

export function HomePage() {
  const { data: user } = useMeQuery()
  const t = useT()
  return (
    <div className="mx-auto max-w-5xl space-y-8">
      <div>
        <h1 className="text-3xl font-bold">
          {t('welcome')}
          {user ? (
            <>
              , <span className="text-gradient">{user.fullName}</span>
            </>
          ) : (
            ''
          )}
        </h1>
        <p className="mt-1 text-muted-foreground">{t('whereToGo')}</p>
      </div>
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {features.map((f, i) => (
          <motion.div
            key={f.to}
            initial={{ opacity: 0, y: 12 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ delay: i * 0.06, duration: 0.3, ease: 'easeOut' }}
          >
            <Link to={f.to}>
              <Card className="h-full gap-2 rounded-2xl transition hover:-translate-y-0.5 hover:border-primary/40 hover:shadow-lg hover:shadow-primary/10">
                <CardHeader>
                  <span className="mb-2 flex size-10 items-center justify-center rounded-xl bg-gradient-to-br from-[var(--chart-1)] to-[var(--chart-3)]">
                    <f.icon className="size-5 text-white" />
                  </span>
                  <CardTitle className="text-lg">{f.title}</CardTitle>
                  <CardDescription>{f.desc}</CardDescription>
                </CardHeader>
              </Card>
            </Link>
          </motion.div>
        ))}
      </div>
    </div>
  )
}
