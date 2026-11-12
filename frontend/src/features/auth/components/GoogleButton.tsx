import { useEffect, useRef } from 'react'
import { useNavigate } from 'react-router-dom'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { useAppDispatch } from '@/app/hooks'
import { useGoogleLoginMutation } from '../authApi'
import { setCredentials } from '../authSlice'

const CLIENT_ID = import.meta.env.VITE_GOOGLE_CLIENT_ID as string | undefined
const GSI_SRC = 'https://accounts.google.com/gsi/client'

// GIS-এর মিনিমাল type (পুরো @types/google.accounts dependency-র দরকার নেই)
declare global {
  interface Window {
    google?: {
      accounts: {
        id: {
          initialize: (config: {
            client_id: string
            callback: (response: { credential: string }) => void
          }) => void
          renderButton: (parent: HTMLElement, options: Record<string, unknown>) => void
        }
      }
    }
  }
}

// GIS script একবারই load (একাধিক mount-এ duplicate <script> না)
let gsiPromise: Promise<void> | null = null
function loadGsi(): Promise<void> {
  gsiPromise ??= new Promise((resolve, reject) => {
    const script = document.createElement('script')
    script.src = GSI_SRC
    script.async = true
    script.onload = () => resolve()
    script.onerror = () => reject(new Error('GIS load failed'))
    document.head.appendChild(script)
  })
  return gsiPromise
}

// Google Sign-In: GIS button → ID token (credential) → আমাদের /auth/google → নিজস্ব session।
// VITE_GOOGLE_CLIENT_ID না থাকলে disabled placeholder (dev-এ চলার মতো, ভাঙে না)।
export function GoogleButton() {
  const containerRef = useRef<HTMLDivElement>(null)
  const [googleLogin] = useGoogleLoginMutation()
  const dispatch = useAppDispatch()
  const navigate = useNavigate()

  useEffect(() => {
    if (!CLIENT_ID || !containerRef.current) return
    let cancelled = false

    loadGsi()
      .then(() => {
        if (cancelled || !window.google || !containerRef.current) return
        window.google.accounts.id.initialize({
          client_id: CLIENT_ID,
          callback: async ({ credential }) => {
            try {
              const res = await googleLogin({ idToken: credential }).unwrap()
              dispatch(setCredentials({ accessToken: res.accessToken }))
              toast.success('লগইন সফল')
              navigate('/')
            } catch {
              toast.error('Google লগইন ব্যর্থ হলো')
            }
          },
        })
        window.google.accounts.id.renderButton(containerRef.current, {
          theme: 'outline',
          size: 'large',
          width: containerRef.current.clientWidth || 320,
          text: 'continue_with',
        })
      })
      .catch(() => toast.error('Google Sign-In load হয়নি'))

    return () => {
      cancelled = true
    }
  }, [googleLogin, dispatch, navigate])

  if (!CLIENT_ID) {
    return (
      <Button
        type="button"
        variant="outline"
        className="w-full gap-2"
        onClick={() => toast.info('Google লগইন এখনো configure হয়নি (VITE_GOOGLE_CLIENT_ID)')}
      >
        <svg className="size-4" viewBox="0 0 24 24" aria-hidden="true">
          <path fill="#4285F4" d="M22.56 12.25c0-.78-.07-1.53-.2-2.25H12v4.26h5.92a5.06 5.06 0 0 1-2.2 3.32v2.77h3.57c2.08-1.92 3.28-4.74 3.28-8.1Z" />
          <path fill="#34A853" d="M12 23c2.97 0 5.46-.98 7.28-2.66l-3.57-2.77c-.98.66-2.23 1.06-3.71 1.06-2.86 0-5.29-1.93-6.16-4.53H2.18v2.84A11 11 0 0 0 12 23Z" />
          <path fill="#FBBC05" d="M5.84 14.1a6.6 6.6 0 0 1 0-4.2V7.06H2.18a11 11 0 0 0 0 9.88l3.66-2.84Z" />
          <path fill="#EA4335" d="M12 5.38c1.62 0 3.06.56 4.21 1.64l3.15-3.15C17.45 2.09 14.97 1 12 1A11 11 0 0 0 2.18 7.06l3.66 2.84C6.71 7.3 9.14 5.38 12 5.38Z" />
        </svg>
        Google দিয়ে চালিয়ে যান
      </Button>
    )
  }

  return <div ref={containerRef} className="flex min-h-10 w-full justify-center" />
}
