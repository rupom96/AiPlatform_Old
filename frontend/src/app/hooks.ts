import { useDispatch, useSelector } from 'react-redux'
import type { RootState, AppDispatch } from './store'

// typed Redux hooks — সবজায়গায় এগুলো ব্যবহার করো (plain useDispatch/useSelector নয়)।
export const useAppDispatch = () => useDispatch<AppDispatch>()
export const useAppSelector = useSelector.withTypes<RootState>()
