import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { ArrowLeft, Loader2, MailCheck } from 'lucide-react'
import { toast } from 'sonner'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { AnimatedBackground } from '@/components/AnimatedBackground'
import { forgotPasswordSchema, type ForgotPasswordValues } from './schemas'
import { useForgotPasswordMutation } from './authApi'

// forgot password — email নাও, generic success দেখাও (account আছে কিনা reveal না, server-ও করে না)।
export function ForgotPasswordPage() {
  const [sent, setSent] = useState(false)
  const [forgotPassword, { isLoading }] = useForgotPasswordMutation()
  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<ForgotPasswordValues>({ resolver: zodResolver(forgotPasswordSchema) })

  const onSubmit = async (values: ForgotPasswordValues) => {
    try {
      await forgotPassword(values).unwrap()
      setSent(true)
    } catch {
      toast.error('অনুরোধ পাঠানো যায়নি — একটু পরে আবার চেষ্টা করুন')
    }
  }

  return (
    <div className="flex min-h-screen items-center justify-center bg-background p-6">
      <AnimatedBackground />
      <Card className="w-full max-w-md rounded-2xl shadow-xl">
        <CardHeader className="space-y-1 text-center">
          <CardTitle className="text-2xl">পাসওয়ার্ড ভুলে গেছেন?</CardTitle>
          <CardDescription>
            {sent ? 'ইমেইল চেক করুন' : 'আপনার ইমেইল দিন — reset link পাঠাব'}
          </CardDescription>
        </CardHeader>
        <CardContent>
          {sent ? (
            <div className="space-y-4 text-center">
              <MailCheck className="mx-auto size-10 text-primary" />
              <p className="text-sm text-muted-foreground">
                এই ইমেইলে অ্যাকাউন্ট থাকলে একটি password reset link পাঠানো হয়েছে। spam folder-ও দেখুন।
              </p>
            </div>
          ) : (
            <form onSubmit={handleSubmit(onSubmit)} className="space-y-4" noValidate>
              <div className="space-y-2">
                <Label htmlFor="forgot-email">ইমেইল</Label>
                <Input
                  id="forgot-email"
                  type="email"
                  placeholder="you@example.com"
                  autoComplete="email"
                  {...register('email')}
                />
                {errors.email && <p className="text-sm text-destructive">{errors.email.message}</p>}
              </div>
              <Button type="submit" className="w-full" disabled={isLoading}>
                {isLoading && <Loader2 className="size-4 animate-spin" />}
                Reset link পাঠান
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
