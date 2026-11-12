import { useState } from 'react'
import { AnimatePresence, motion } from 'framer-motion'
import { Sparkles } from 'lucide-react'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { LoginForm } from './components/LoginForm'
import { RegisterForm } from './components/RegisterForm'

type Mode = 'login' | 'register'

export default function AuthPage({ initialMode = 'login' }: { initialMode?: Mode }) {
  const [mode, setMode] = useState<Mode>(initialMode)
  const isLogin = mode === 'login'

  return (
    <div className="grid min-h-screen bg-background lg:grid-cols-2">
      {/* Left — brand panel (mobile-এ লুকানো) */}
      <div className="relative hidden flex-col justify-between overflow-hidden bg-gradient-to-br from-indigo-600 via-purple-600 to-fuchsia-600 p-12 text-white lg:flex">
        <div className="flex items-center gap-2 text-lg font-semibold">
          <Sparkles className="size-5" /> AiPlatform
        </div>
        <div className="space-y-4">
          <h2 className="text-4xl font-bold leading-tight">
            আপনার AI সহকারী,
            <br /> এক জায়গায়।
          </h2>
          <p className="max-w-md text-white/80">
            Chat, structured analysis, আর নিজের ডকুমেন্ট থেকে RAG উত্তর — সব একসাথে।
          </p>
        </div>
        <p className="text-sm text-white/60">© AiPlatform</p>
        <div className="pointer-events-none absolute -right-24 -top-24 size-72 rounded-full bg-white/10 blur-3xl" />
        <div className="pointer-events-none absolute -bottom-24 -left-16 size-72 rounded-full bg-white/10 blur-3xl" />
      </div>

      {/* Right — auth card */}
      <div className="flex items-center justify-center p-6">
        <Card className="w-full max-w-md rounded-2xl shadow-xl">
          <CardHeader className="space-y-1 text-center">
            <CardTitle className="text-2xl">{isLogin ? 'Welcome Back' : 'অ্যাকাউন্ট তৈরি করুন'}</CardTitle>
            <CardDescription>
              {isLogin ? 'লগইন করে চালিয়ে যান' : 'শুরু করতে কিছু তথ্য দিন'}
            </CardDescription>
          </CardHeader>
          <CardContent>
            <AnimatePresence mode="wait">
              <motion.div
                key={mode}
                initial={{ opacity: 0, x: isLogin ? -16 : 16 }}
                animate={{ opacity: 1, x: 0 }}
                exit={{ opacity: 0, x: isLogin ? 16 : -16 }}
                transition={{ duration: 0.2 }}
              >
                {isLogin ? (
                  <LoginForm onSwitch={() => setMode('register')} />
                ) : (
                  <RegisterForm onSwitch={() => setMode('login')} />
                )}
              </motion.div>
            </AnimatePresence>
          </CardContent>
        </Card>
      </div>
    </div>
  )
}
