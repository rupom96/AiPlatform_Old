import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { ArrowLeft, Loader2 } from 'lucide-react'
import { toast } from 'sonner'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Label } from '@/components/ui/label'
import { AnimatedBackground } from '@/components/AnimatedBackground'
import { PasswordInput } from './components/PasswordInput'
import { resetPasswordSchema, type ResetPasswordValues } from './schemas'
import { useResetPasswordMutation } from './authApi'

// reset password — email-এর link থেকে আসে (?email=..&token=..)। নতুন password সেট → login।
export function ResetPasswordPage() {
  const [params] = useSearchParams()
  const email = params.get('email') ?? ''
  const token = params.get('token') ?? ''
  const navigate = useNavigate()
  const [resetPassword, { isLoading }] = useResetPasswordMutation()
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<ResetPasswordValues>({ resolver: zodResolver(resetPasswordSchema) })

  const linkBroken = !email || !token

  const onSubmit = async (values: ResetPasswordValues) => {
    try {
      await resetPassword({ email, token, newPassword: values.password }).unwrap()
      toast.success('পাসওয়ার্ড বদলানো হয়েছে — নতুন পাসওয়ার্ড দিয়ে লগইন করুন')
      navigate('/login')
    } catch {
      // token খারাপ/expired বা policy fail — generic (server details leak করে না)
      toast.error('Reset করা যায়নি — link-টা expired হতে পারে, আবার চেষ্টা করুন')
    }
  }

  return (
    <div className="flex min-h-screen items-center justify-center bg-background p-6">
      <AnimatedBackground />
      <Card className="w-full max-w-md rounded-2xl shadow-xl">
        <CardHeader className="space-y-1 text-center">
          <CardTitle className="text-2xl">নতুন পাসওয়ার্ড</CardTitle>
          <CardDescription>
            {linkBroken ? 'Link-এ সমস্যা' : `${email}-এর জন্য নতুন পাসওয়ার্ড দিন`}
          </CardDescription>
        </CardHeader>
        <CardContent>
          {linkBroken ? (
            <p className="text-center text-sm text-muted-foreground">
              Reset link অসম্পূর্ণ বা ভাঙা। ইমেইলের link-টা হুবহু খুলুন, অথবা{' '}
              <Link to="/forgot-password" className="font-medium text-foreground hover:underline">
                নতুন link নিন
              </Link>
              ।
            </p>
          ) : (
            <form onSubmit={handleSubmit(onSubmit)} className="space-y-4" noValidate>
              <div className="space-y-2">
                <Label htmlFor="new-password">নতুন পাসওয়ার্ড</Label>
                <PasswordInput id="new-password" autoComplete="new-password" {...register('password')} />
                {errors.password && <p className="text-sm text-destructive">{errors.password.message}</p>}
              </div>
              <div className="space-y-2">
                <Label htmlFor="confirm-password">পাসওয়ার্ড আবার দিন</Label>
                <PasswordInput id="confirm-password" autoComplete="new-password" {...register('confirmPassword')} />
                {errors.confirmPassword && (
                  <p className="text-sm text-destructive">{errors.confirmPassword.message}</p>
                )}
              </div>
              <Button type="submit" className="w-full" disabled={isLoading}>
                {isLoading && <Loader2 className="size-4 animate-spin" />}
                পাসওয়ার্ড বদলান
              </Button>
            </form>
          )}
          <p className="mt-4 text-center text-sm text-muted-foreground">
            <Link to="/login" className="inline-flex items-center gap-1 font-medium text-foreground hover:underline">
              <ArrowLeft className="size-3.5" /> লগইনে ফিরুন
            </Link>
          </p>
        </CardContent>
      </Card>
    </div>
  )
}
