import { Link, NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { AnimatePresence, motion } from 'framer-motion'
import { toast } from 'sonner'
import {
  // BarChart3,   // hidden: Dashboard — docs/hidden-features.md
  Home,
  LogOut,
  MessageSquare,
  Settings,
  Sparkles,
} from 'lucide-react'
import { cn } from '@/lib/utils'
import { useAppDispatch } from '@/app/hooks'
import { logOut } from '@/features/auth/authSlice'
import { useLogoutMutation, useMeQuery } from '@/features/auth/authApi'
import { useT } from '@/i18n/useT'
import { Button } from '@/components/ui/button'
import { AnimatedBackground } from './AnimatedBackground'
import { ThemeToggle } from './ThemeToggle'

const navItems = [
  { to: '/', title: 'Overview', icon: Home },
  { to: '/chat', title: 'Chat', icon: MessageSquare },
  // { to: '/dashboard', title: 'Dashboard', icon: BarChart3 },   // hidden — docs/hidden-features.md
  { to: '/settings', title: 'Settings', icon: Settings },
]

function NavItems({ onNavigate }: { onNavigate?: () => void }) {
  return (
    <>
      {navItems.map((item) => (
        <NavLink
          key={item.to}
          to={item.to}
          end={item.to === '/'}
          onClick={onNavigate}
          className={({ isActive }) =>
            cn(
              'flex items-center gap-3 rounded-2xl px-4 py-2.5 text-sm transition-colors',
              isActive
                ? 'bg-primary/15 font-medium text-primary'
                : 'text-muted-foreground hover:bg-accent hover:text-foreground',
            )
          }
        >
          <item.icon className="size-4 shrink-0" />
          <span>{item.title}</span>
        </NavLink>
      ))}
    </>
  )
}

// protected app layout — glass sidebar (nav + user footer) + animated mesh bg + page transition।
export function AppShell() {
  const { data: user } = useMeQuery()
  const [logout] = useLogoutMutation()
  const dispatch = useAppDispatch()
  const navigate = useNavigate()
  const location = useLocation()
  const t = useT()

  const handleLogout = async () => {
    try {
      await logout().unwrap()
    } catch {
      /* logout best-effort */
    }
    dispatch(logOut())
    toast.success('লগআউট হয়েছে')
    navigate('/login')
  }

  const initials = (user?.fullName ?? '?')
    .split(' ')
    .map((w) => w[0])
    .slice(0, 2)
    .join('')
    .toUpperCase()

  return (
    <div className="min-h-screen bg-background text-foreground">
      <AnimatedBackground />

      <div className="mx-auto flex min-h-screen max-w-[1600px]">
        {/* --- sidebar (lg+) --- */}
        <aside className="sticky top-0 hidden h-screen w-60 shrink-0 flex-col gap-2 border-r bg-card p-4 backdrop-blur-xl lg:flex">
          <Link to="/" className="mb-4 flex items-center gap-2 px-2 font-semibold">
            <span className="flex size-8 items-center justify-center rounded-xl bg-gradient-to-br from-[var(--chart-1)] to-[var(--chart-3)]">
              <Sparkles className="size-4 text-white" />
            </span>
            AiPlatform
          </Link>

          <nav className="flex flex-1 flex-col gap-1">
            <NavItems />
          </nav>

          <div className="flex items-center gap-3 border-t pt-4">
            <span className="flex size-9 shrink-0 items-center justify-center rounded-full bg-gradient-to-br from-[var(--chart-1)] to-[var(--chart-3)] text-xs font-bold text-white">
              {initials}
            </span>
            <div className="min-w-0 flex-1">
              <p className="truncate text-sm font-medium">{user?.fullName}</p>
              <p className="truncate text-xs text-muted-foreground">{user?.email}</p>
            </div>
            <ThemeToggle />
            <Button variant="ghost" size="icon" onClick={handleLogout} aria-label={t('logout')}>
              <LogOut className="size-4" />
            </Button>
          </div>
        </aside>

        {/* --- main --- */}
        <div className="flex min-w-0 flex-1 flex-col">
          {/* mobile header (<lg) */}
          <header className="sticky top-0 z-10 flex items-center justify-between gap-2 border-b bg-card px-4 py-3 backdrop-blur-xl lg:hidden">
            <Link to="/" className="flex items-center gap-2 font-semibold">
              <span className="flex size-7 items-center justify-center rounded-lg bg-gradient-to-br from-[var(--chart-1)] to-[var(--chart-3)]">
                <Sparkles className="size-3.5 text-white" />
              </span>
              AiPlatform
            </Link>
            <div className="flex items-center gap-1">
              <ThemeToggle />
              <Button variant="ghost" size="icon" onClick={handleLogout} aria-label={t('logout')}>
                <LogOut className="size-4" />
              </Button>
            </div>
          </header>
          {/* mobile nav strip */}
          <nav className="flex gap-1 overflow-x-auto border-b bg-card px-3 py-2 backdrop-blur-xl lg:hidden">
            <NavItems />
          </nav>

          {/* page transition — pathname বদলালে fade+rise */}
          <AnimatePresence mode="wait">
            <motion.main
              key={location.pathname}
              initial={{ opacity: 0, y: 8 }}
              animate={{ opacity: 1, y: 0 }}
              exit={{ opacity: 0, y: -8 }}
              transition={{ duration: 0.18, ease: 'easeOut' }}
              className="flex-1 p-4 sm:p-6 lg:p-8"
            >
              <Outlet />
            </motion.main>
          </AnimatePresence>
        </div>
      </div>
    </div>
  )
}
