import { useEffect, type ReactNode } from 'react'
import { Loader2 } from 'lucide-react'
import { useAppDispatch, useAppSelector } from '@/app/hooks'
import { setBootstrapped, setCredentials, selectBootstrapped } from './authSlice'

// App load-এ একবার silent refresh (cookie দিয়ে) — valid session থাকলে auto login।
// bootstrap শেষ না হওয়া পর্যন্ত loader (নাহলে flash হয়ে login দেখাবে)।
export function AppBootstrap({ children }: { children: ReactNode }) {
  const dispatch = useAppDispatch()
  const bootstrapped = useAppSelector(selectBootstrapped)

  useEffect(() => {
    let active = true
    void (async () => {
      try {
        const res = await fetch(`${import.meta.env.VITE_API_BASE_URL}/api/v1/auth/refresh`, {
          method: 'POST',
          credentials: 'include',
        })
        if (active && res.ok) {
          const data = (await res.json()) as { accessToken: string }
          dispatch(setCredentials({ accessToken: data.accessToken }))
        }
      } catch {
        /* কোনো valid session নেই — unauthenticated */
      }
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
