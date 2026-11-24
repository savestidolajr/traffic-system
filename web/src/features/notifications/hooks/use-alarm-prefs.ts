import { useState, useCallback } from 'react'

const SOUND_KEY = 'arn.alarm.sound'
const DESKTOP_KEY = 'arn.alarm.desktop'

function readPref(key: string, fallback: boolean): boolean {
    try {
        const v = localStorage.getItem(key)
        return v !== null ? v === 'true' : fallback
    } catch {
        return fallback
    }
}

function writePref(key: string, value: boolean) {
    try {
        localStorage.setItem(key, String(value))
    } catch {
        // localStorage may be blocked; silently ignore
    }
}

export function useAlarmPrefs() {
    const [soundEnabled, setSoundEnabled] = useState(() =>
        readPref(SOUND_KEY, true)
    )
    const [desktopEnabled, setDesktopEnabled] = useState(() =>
        readPref(DESKTOP_KEY, false)
    )

    const toggleSound = useCallback(() => {
        setSoundEnabled((prev) => {
            const next = !prev
            writePref(SOUND_KEY, next)
            return next
        })
    }, [])

    const toggleDesktop = useCallback(() => {
        setDesktopEnabled((prev) => {
            const next = !prev
            writePref(DESKTOP_KEY, next)
            return next
        })
    }, [])

    return { soundEnabled, desktopEnabled, toggleSound, toggleDesktop } as const
}
