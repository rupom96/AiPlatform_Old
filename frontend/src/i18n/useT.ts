import { useAppSelector } from '@/app/hooks'
import { selectLanguage } from '@/features/settings/uiSlice'
import { strings, type StringKey } from './strings'

// t('key') → বর্তমান ভাষার string। key অজানা হলে key ফেরত (fail-safe)।
export function useT() {
  const lang = useAppSelector(selectLanguage)
  return (key: StringKey) => strings[lang][key] ?? key
}
