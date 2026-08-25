import { createContext, useCallback, useContext, useRef, useState } from 'react'

import { ApiError } from './api'

interface Toast {
  id: number
  message: string
}

interface ToastContextValue {
  /** Shows a short-lived message strip at the bottom of the screen. */
  toast: (message: string) => void
  /** Runs an api call and turns its failure into a toast instead of a crash. */
  run: (action: Promise<unknown>) => Promise<void>
}

const ToastContext = createContext<ToastContextValue>({
  toast: () => undefined,
  run: async () => undefined,
})

export const useToast = () => useContext(ToastContext)

export function ToastProvider({ children }: { children: React.ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([])
  const counter = useRef(0)

  const toast = useCallback((message: string) => {
    const id = ++counter.current

    setToasts(current => [...current, { id, message }])

    window.setTimeout(() => {
      setToasts(current => current.filter(t => t.id !== id))
    }, 4000)
  }, [])

  const run = useCallback(
    async (action: Promise<unknown>) => {
      try {
        await action
      } catch (error) {
        toast(error instanceof ApiError ? error.message : 'Nastala neznámá chyba')
      }
    },
    [toast],
  )

  return (
    <ToastContext.Provider value={{ toast, run }}>
      {children}
      <div className="toasts" role="status">
        {toasts.map(t => (
          <div key={t.id} className="toast">
            {t.message}
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  )
}
