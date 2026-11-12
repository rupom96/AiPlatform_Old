import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { Loader2 } from 'lucide-react'
import { toast } from 'sonner'
import { useNavigate } from 'react-router-dom'
import { loginSchema, type LoginValues } from '../schemas'
import { useLoginMutation } from '../authApi'
import { setCredentials } from '../authSlice'
import { useAppDispatch } from '@/app/hooks'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { PasswordInput } from './PasswordInput'
import { GoogleButton } from './GoogleButton'

export function LoginForm({ onSwitch }: { onSwitch: () => void }) {
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<LoginValues>({
    resolver: zodResolver(loginSchema),
    defaultValues: { rememberMe: false },
  })
  const [login, { isLoading }] = useLoginMutation()
  const dispatch = useAppDispatch()
  const navigate = useNavigate()

  const onSubmit = async (values: LoginValues) => {
    try {
      const res = await login(values).unwrap()
      dispatch(setCredentials({ accessToken: res.accessToken }))
      toast.success('লগইন সফল')
      navigate('/')
    } catch {
      toast.error('ইমেইল বা পাসওয়ার্ড ভুল') // generic — server-ও generic দেয়
    }
  }

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="space-y-4" noValidate>
      <div className="space-y-2">
        <Label htmlFor="email">ইমেইল</Label>
        <Input id="email" type="email" placeholder="you@example.com" autoComplete="email" {...register('email')} />
        {errors.email && <p className="text-sm text-destructive">{errors.email.message}</p>}
      </div>

      <div className="space-y-2">
        <div className="flex items-center justify-between">
          <Label htmlFor="password">পাসওয়ার্ড</Label>
          <button
            type="button"
            onClick={() => navigate('/forgot-password')}
            className="text-sm text-muted-foreground hover:text-foreground"
          >
            পাসওয়ার্ড ভুলে গেছেন?
          </button>
        </div>
        <PasswordInput id="password" autoComplete="current-password" {...register('password')} />
        {errors.password && <p className="text-sm text-destructive">{errors.password.message}</p>}
      </div>

      {/* remember me → refresh cookie persistent হয় (browser বন্ধেও session টেকে) */}
      <label className="flex items-center gap-2 text-sm text-muted-foreground">
        <input type="checkbox" className="size-4 rounded border-input" {...register('rememberMe')} /> মনে রাখো
      </label>

      <Button type="submit" className="w-full" disabled={isLoading}>
        {isLoading && <Loader2 className="size-4 animate-spin" />}
        লগইন
      </Button>

      <div className="relative py-1 text-center text-xs text-muted-foreground">
        <span className="relative z-10 bg-card px-2">অথবা</span>
        <div className="absolute inset-x-0 top-1/2 h-px bg-border" />
      </div>

      <GoogleButton />

      <p className="text-center text-sm text-muted-foreground">
        অ্যাকাউন্ট নেই?{' '}
        <button type="button" onClick={onSwitch} className="font-medium text-foreground hover:underline">
          রেজিস্টার করুন
        </button>
      </p>
    </form>
  )
}
