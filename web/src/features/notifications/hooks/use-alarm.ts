import { useEffect, useRef } from 'react'
import { useLocation } from 'react-router'
import { useLiveJobs } from '@/features/jobs/api/job-hooks'
import { ROUTE_JOBS } from '@/lib/routes'
import { getApprovalDeadline } from '@/features/jobs/lib/job-meta/urgency'
import { formatMarketName } from '@/lib/formatting'
import { ALARM_CHIME, SILENT_CHIME, CHIME_VOLUME } from '../constants'
import { useJobStatusTransitions } from './use-job-status-transitions'

let sharedAudioContext: AudioContext | null = null
let resumeListenerAttached = false

/**
 * Lazily create the single, app-lifetime AudioContext shared by every
 * chime. Browsers cap the number of concurrent AudioContexts (Chrome
 * allows ~6); creating one per chime silently breaks alarms after a
 * handful of notifications. This context is intentionally never closed —
 * it lives for the app's lifetime and is reused by every chime.
 */
function getSharedAudioContext(): AudioContext {
    if (!sharedAudioContext) {
        sharedAudioContext = new AudioContext()
    }
    return sharedAudioContext
}

/**
 * Create a two-tone chime via Web Audio API.
 * Returns false if audio context could not be created/started.
 */
function playChime(
    freq1: number,
    dur1: number,
    freq2: number,
    dur2: number
): boolean {
    try {
        const ctx = getSharedAudioContext()
        // Resume if suspended (autoplay policy). At most one resume
        // listener is ever pending, so repeated chime attempts while
        // suspended can't stack listeners on `document`.
        if (ctx.state === 'suspended') {
            if (!resumeListenerAttached) {
                resumeListenerAttached = true
                document.addEventListener(
                    'pointerdown',
                    () => {
                        resumeListenerAttached = false
                        void ctx.resume()
                    },
                    { once: true }
                )
            }
            return false
        }

        const gain = ctx.createGain()
        gain.connect(ctx.destination)
        gain.gain.setValueAtTime(0, ctx.currentTime)
        gain.gain.linearRampToValueAtTime(CHIME_VOLUME, ctx.currentTime + 0.01)
        gain.gain.linearRampToValueAtTime(
            CHIME_VOLUME,
            ctx.currentTime + dur1 / 1000
        )
        gain.gain.setValueAtTime(0, ctx.currentTime + dur1 / 1000 + 0.02)

        const osc1 = ctx.createOscillator()
        osc1.type = 'sine'
        osc1.frequency.setValueAtTime(freq1, ctx.currentTime)
        osc1.connect(gain)
        osc1.start(ctx.currentTime)
        osc1.stop(ctx.currentTime + dur1 / 1000)
        osc1.onended = () => osc1.disconnect()

        const osc2 = ctx.createOscillator()
        osc2.type = 'sine'
        osc2.frequency.setValueAtTime(freq2, ctx.currentTime + dur1 / 1000)
        osc2.connect(gain)
        osc2.start(ctx.currentTime + dur1 / 1000)
        osc2.stop(ctx.currentTime + dur1 / 1000 + dur2 / 1000)
        osc2.onended = () => {
            osc2.disconnect()
            gain.disconnect()
        }

        return true
    } catch {
        return false
    }
}

/**
 * React to new WAITING_APPROVAL / FAILED jobs with audio + desktop notifications.
 *
 * The shared `useJobStatusTransitions` hook primes its seen set on first data
 * without firing alarms.
 *
 * @param opts.soundEnabled    — whether to play audio chimes
 * @param opts.desktopEnabled  — whether to show OS desktop notifications
 * @param opts.navigate        — router navigate function for notification onclick
 */
export function useAlarm(
    opts: {
        soundEnabled?: boolean
        desktopEnabled?: boolean
        navigate?: (jobId: string) => void
    } = {}
) {
    const { soundEnabled = true, desktopEnabled = false, navigate } = opts
    const { data: jobsData } = useLiveJobs()
    const transitions = useJobStatusTransitions(jobsData?.jobs)
    const processedTransitionsRef = useRef<Set<string>>(new Set())

    // Read through a ref so navigation doesn't re-run the alarm effect.
    const { pathname } = useLocation()
    const pathnameRef = useRef(pathname)
    // Sync ref after commit so the alarm effect reads latest committed pathname
    useEffect(() => {
        pathnameRef.current = pathname
    })

    useEffect(() => {
        for (const job of transitions) {
            const key = `${job.jobId}:${job.status}`
            if (processedTransitionsRef.current.has(key)) continue
            processedTransitionsRef.current.add(key)

            // No chime for a transition the operator is watching happen:
            // their own revise/retry landing back in the open cockpit of a
            // visible tab is news the screen already delivered. (Still marked
            // processed above, so navigating away doesn't retro-fire it.)
            if (
                !document.hidden &&
                pathnameRef.current === `${ROUTE_JOBS}/${job.jobId}`
            ) {
                continue
            }

            if (job.status === 'WAITING_APPROVAL') {
                // Two-tone chime: 880Hz 120ms → 1175Hz 180ms
                if (soundEnabled) {
                    playChime(
                        ALARM_CHIME.freq1,
                        ALARM_CHIME.dur1,
                        ALARM_CHIME.freq2,
                        ALARM_CHIME.dur2
                    )
                }

                // Desktop notification (only when tab hidden)
                if (
                    desktopEnabled &&
                    'Notification' in window &&
                    Notification.permission === 'granted' &&
                    document.hidden
                ) {
                    const deadline = getApprovalDeadline(job)
                    const timeStr = deadline
                        ? `${deadline.getHours().toString().padStart(2, '0')}:${deadline.getMinutes().toString().padStart(2, '0')}`
                        : '-'
                    const notif = new Notification('Bulletin needs review', {
                        body: `${job.station} ${formatMarketName(job.market)}. Approve by ${timeStr}`,
                        tag: job.jobId,
                    })
                    notif.onclick = () => {
                        window.focus()
                        navigate?.(job.jobId)
                        notif.close()
                    }
                }
            } else if (job.status === 'FAILED') {
                // Lower-pitch chime: 440Hz 150ms → 660Hz 120ms
                if (soundEnabled) {
                    playChime(
                        SILENT_CHIME.freq1,
                        SILENT_CHIME.dur1,
                        SILENT_CHIME.freq2,
                        SILENT_CHIME.dur2
                    )
                }
            }
        }
    }, [transitions, soundEnabled, desktopEnabled, navigate])
}
