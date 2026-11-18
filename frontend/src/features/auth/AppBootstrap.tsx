import { useEffect, type ReactNode } from 'react'
import { Loader2 } from 'lucide-react'
import { useAppDispatch, useAppSelector } from '@/app/hooks'
import { setBootstrapped, setCredentials, selectBootstrapped } from './authSlice'
import { refreshAccessToken } from './refreshSession'

// App load-এ একবার silent refresh (cookie দিয়ে) — valid session থাকলে auto login।
// bootstrap শেষ না হওয়া পর্যন্ত loader (নাহলে flash হয়ে login দেখাবে)।
export function AppBootstrap({ children }: { children: ReactNode }) {
  const dispatch = useAppDispatch()
  const bootstrapped = useAppSelector(selectBootstrapped)

  useEffect(() => {
    let active = true
    void (async () => {
      // StrictMode-এ এই effect দুইবার চলে; refreshAccessToken দুই call-কে এক request-এ মিলিয়ে দেয়
      // (দুটো আলাদা request গেলে দ্বিতীয়টা "reuse" ধরা পড়ে session বাতিল করত)।
      const accessToken = await refreshAccessToken()
      if (active && accessToken) dispatch(setCredentials({ accessToken }))
      if (active) dispatch(setBootstrapped())
    })()
    return () => {
      active = false
    }
  }, [dispatch])

  if (!bootstrapped) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-background">
        <Loader2 className="size-6 animate-spin text-muted-foreground" />
      </div>
    )
  }

  return <>{children}</>
}
