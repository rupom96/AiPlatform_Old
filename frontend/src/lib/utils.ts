import { clsx, type ClassValue } from 'clsx'
import { twMerge } from 'tailwind-merge'

// shadcn/ui helper — conditional + conflict-free Tailwind class merge।
export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs))
}
