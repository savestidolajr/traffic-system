import { useCallback } from 'react'
import { useNavigate } from 'react-router'
import { ROUTE_JOBS } from '@/lib/routes'
import { useAlarm } from '../hooks/use-alarm'
import { useAlarmPrefs } from '../hooks/use-alarm-prefs'
import { useTitleAlarm } from '../hooks/use-title-alarm'

/**
 * Renderless component that wires up alarm effects (audio chimes, desktop
 * notifications, title countdown). Must be mounted inside BrowserRouter
 * so `useNavigate` works.
 */
export function AlarmEffects() {
    const { soundEnabled, desktopEnabled } = useAlarmPrefs()
    const navigate = useNavigate()
    const handleNavigate = useCallback(
        (jobId: string) => navigate(ROUTE_JOBS + '/' + jobId),
        [navigate]
    )

    useAlarm({
        soundEnabled,
        desktopEnabled,
        navigate: handleNavigate,
    })

    useTitleAlarm()

    return null
}
