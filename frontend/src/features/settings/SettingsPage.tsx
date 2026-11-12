import { useAppDispatch, useAppSelector } from '@/app/hooks'
import { useT } from '@/i18n/useT'
import type { Lang } from '@/i18n/strings'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Label } from '@/components/ui/label'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import {
  selectDefaultProvider,
  selectLanguage,
  selectTheme,
  setDefaultProvider,
  setLanguage,
  setTheme,
  type Theme,
} from './uiSlice'

import { PROVIDERS } from '@/lib/providers'

export function SettingsPage() {
  const t = useT()
  const dispatch = useAppDispatch()
  const language = useAppSelector(selectLanguage)
  const theme = useAppSelector(selectTheme)
  const defaultProvider = useAppSelector(selectDefaultProvider)

  return (
    <div className="mx-auto max-w-lg space-y-6">
      <div>
        <h1 className="text-2xl font-bold">{t('settings')}</h1>
        <p className="text-sm text-muted-foreground">{t('settingsHint')}</p>
      </div>

      <Card>
        <CardHeader>
          <CardTitle className="text-lg">{t('settings')}</CardTitle>
        </CardHeader>
        <CardContent className="space-y-5">
          <div className="flex items-center justify-between gap-4">
            <Label>{t('language')}</Label>
            <Select value={language} onValueChange={(v) => dispatch(setLanguage(v as Lang))}>
              <SelectTrigger className="w-40">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="bn">বাংলা</SelectItem>
                <SelectItem value="en">English</SelectItem>
              </SelectContent>
            </Select>
          </div>

          <div className="flex items-center justify-between gap-4">
            <Label>{t('theme')}</Label>
            <Select value={theme} onValueChange={(v) => dispatch(setTheme(v as Theme))}>
              <SelectTrigger className="w-40">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="light">{t('light')}</SelectItem>
                <SelectItem value="dark">{t('dark')}</SelectItem>
              </SelectContent>
            </Select>
          </div>

          <div className="flex items-center justify-between gap-4">
            <Label>{t('defaultProvider')}</Label>
            <Select value={defaultProvider} onValueChange={(v) => dispatch(setDefaultProvider(v))}>
              <SelectTrigger className="w-40">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {PROVIDERS.map((p) => (
                  <SelectItem key={p} value={p}>
                    {p}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
        </CardContent>
      </Card>
    </div>
  )
}
