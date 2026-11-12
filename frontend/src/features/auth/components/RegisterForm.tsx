import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { Loader2 } from 'lucide-react'
import { toast } from 'sonner'
import { registerSchema, type RegisterValues } from '../schemas'
import { useRegisterMutation } from '../authApi'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { PasswordInput } from './PasswordInput'
import { GoogleButton } from './GoogleButton'

export function RegisterForm({ onSwitch }: { onSwitch: () => void }) {
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<RegisterValues>({ resolver: zodResolver(registerSchema) })
  const [registerUser, { isLoading }] = useRegisterMutation()

  const onSubmit = async (values: RegisterValues) => {
    try {
      await registerUser(values).unwrap()
      toast.success('অ্যাকাউন্ট তৈরি হয়েছে — এবার লগইন করুন')
      onSwitch()
    } catch (err) {
      const message =
        (err as { data?: { title?: string } })?.data?.title ?? 'রেজিস্ট্রেশন ব্যর্থ হয়েছে'
      toast.error(message)
    }
  }

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="space-y-4" noValidate>
      <div className="space-y-2">
        <Label htmlFor="fullName">নাম</Label>
        <Input id="fullName" placeholder="আপনার নাম" autoComplete="name" {...register('fullName')} />
        {errors.fullName && <p className="text-sm text-destructive">{errors.fullName.message}</p>}
      </div>

      <div className="space-y-2">
        <Label htmlFor="reg-email">ইমেইল</Label>
        <Input id="reg-email" type="email" placeholder="you@example.com" autoComplete="email" {...register('email')} />
        {errors.email && <p className="text-sm text-destructive">{errors.email.message}</p>}
      </div>

      <div className="space-y-2">
        <Label htmlFor="reg-password">পাসওয়ার্ড</Label>
        <PasswordInput id="reg-password" autoComplete="new-password" {...register('password')} />
        {errors.password && <p className="text-sm text-destructive">{errors.password.message}</p>}
      </div>

      <Button type="submit" className="w-full" disabled={isLoading}>
        {isLoading && <Loader2 className="size-4 animate-spin" />}
        অ্যাকাউন্ট তৈরি করুন
      </Button>

      <div className="relative py-1 text-center text-xs text-muted-foreground">
        <span className="relative z-10 bg-card px-2">অথবা</span>
        <div className="absolute inset-x-0 top-1/2 h-px bg-border" />
      </div>

      <GoogleButton />

      <p className="text-center text-sm text-muted-foreground">
        আগে থেকেই অ্যাকাউন্ট আছে?{' '}
        <button type="button" onClick={onSwitch} className="font-medium text-foreground hover:underline">
          লগইন করুন
        </button>
      </p>
    </form>
  )
}
