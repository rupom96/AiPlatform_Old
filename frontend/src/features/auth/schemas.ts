import { z } from 'zod'

export const loginSchema = z.object({
  email: z.string().min(1, 'ইমেইল দিন').email('সঠিক ইমেইল দিন'),
  password: z.string().min(1, 'পাসওয়ার্ড দিন'),
  rememberMe: z.boolean(),   // default useForm defaultValues-এ (zod .default() rhf resolver-এর type ভাঙে)
})

export const forgotPasswordSchema = z.object({
  email: z.string().min(1, 'ইমেইল দিন').email('সঠিক ইমেইল দিন'),
})

// backend password policy-র সাথে মিল (min 8, upper/lower/digit/symbol)
export const registerSchema = z.object({
  fullName: z.string().min(1, 'নাম দিন').max(100),
  email: z.string().min(1, 'ইমেইল দিন').email('সঠিক ইমেইল দিন'),
  password: z
    .string()
    .min(8, 'কমপক্ষে ৮ অক্ষর')
    .regex(/[A-Z]/, 'একটি বড় হাতের অক্ষর দরকার')
    .regex(/[a-z]/, 'একটি ছোট হাতের অক্ষর দরকার')
    .regex(/[0-9]/, 'একটি সংখ্যা দরকার')
    .regex(/[^A-Za-z0-9]/, 'একটি বিশেষ চিহ্ন দরকার'),
})

// reset password — register-এর password policy-ই, + confirm মিল
export const resetPasswordSchema = z
  .object({
    password: registerSchema.shape.password,
    confirmPassword: z.string().min(1, 'পাসওয়ার্ড আবার দিন'),
  })
  .refine((v) => v.password === v.confirmPassword, {
    message: 'পাসওয়ার্ড মেলেনি',
    path: ['confirmPassword'],
  })

export type LoginValues = z.infer<typeof loginSchema>
export type RegisterValues = z.infer<typeof registerSchema>
export type ForgotPasswordValues = z.infer<typeof forgotPasswordSchema>
export type ResetPasswordValues = z.infer<typeof resetPasswordSchema>
