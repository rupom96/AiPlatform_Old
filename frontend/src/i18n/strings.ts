// lean i18n — representative UI strings (app Bengali-first; বাকি string incrementally যোগ হবে)।
export type Lang = 'bn' | 'en'

export const strings = {
  bn: {
    welcome: 'স্বাগতম',
    whereToGo: 'কোথায় যেতে চান?',
    logout: 'লগআউট',
    settings: 'সেটিংস',
    language: 'ভাষা',
    theme: 'থিম',
    defaultProvider: 'ডিফল্ট প্রোভাইডার',
    light: 'লাইট',
    dark: 'ডার্ক',
    settingsHint: 'পছন্দগুলো এই ব্রাউজারে সংরক্ষিত থাকে।',
  },
  en: {
    welcome: 'Welcome',
    whereToGo: 'Where do you want to go?',
    logout: 'Logout',
    settings: 'Settings',
    language: 'Language',
    theme: 'Theme',
    defaultProvider: 'Default provider',
    light: 'Light',
    dark: 'Dark',
    settingsHint: 'Preferences are saved in this browser.',
  },
} as const

export type StringKey = keyof (typeof strings)['bn']
