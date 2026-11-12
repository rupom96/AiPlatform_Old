import { Moon, Sun } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { useAppDispatch, useAppSelector } from '@/app/hooks'
import { selectTheme, setTheme } from '@/features/settings/uiSlice'

// theme uiSlice-এ; toggle → dispatch (store.subscribe DOM/localStorage apply করে)।
export function ThemeToggle() {
  const theme = useAppSelector(selectTheme)
  const dispatch = useAppDispatch()

  return (
    <Button
      variant="ghost"
      size="icon"
      onClick={() => dispatch(setTheme(theme === 'dark' ? 'light' : 'dark'))}
      aria-label="থিম বদলান"
    >
      {theme === 'dark' ? <Sun className="size-4" /> : <Moon className="size-4" />}
    </Button>
  )
}
