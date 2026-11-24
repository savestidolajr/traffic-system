import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import type { StudioStep } from './Studio'

/**
 * Gates each step's invalid indicator behind user interaction, so a freshly
 * opened studio never greets the operator with amber warnings for steps they
 * have not looked at yet. A step's `invalid` flag is revealed once it has
 * been touched, or once a submit has been attempted.
 *
 * `open` resets the gate when the dialog reopens; pass it for studios whose
 * hook state outlives the dialog. Studios mounted inside dialog content
 * remount on every open and can omit it.
 */
export function useStudioStepGate(
    steps: readonly StudioStep[],
    activeId: string,
    open?: boolean
): {
    readonly steps: readonly StudioStep[]
    readonly onSelectStep: (id: string) => void
    readonly onSubmitAttempt: () => void
} {
    const [submitAttempted, setSubmitAttempted] = useState(false)
    const [touched, setTouched] = useState<ReadonlySet<string>>(new Set())
    const prevActiveRef = useRef(activeId)
    const wasOpenRef = useRef(open ?? true)

    useEffect(() => {
        if (open && !wasOpenRef.current) {
            setSubmitAttempted(false)
            setTouched(new Set())
            prevActiveRef.current = activeId
        }
        wasOpenRef.current = open ?? true
    }, [open, activeId])

    useEffect(() => {
        if (prevActiveRef.current !== activeId) {
            setTouched((prev) => new Set(prev).add(prevActiveRef.current))
            prevActiveRef.current = activeId
        }
    }, [activeId])

    const onSelectStep = useCallback((id: string) => {
        setTouched((prev) => new Set(prev).add(id))
    }, [])

    const onSubmitAttempt = useCallback(() => {
        setSubmitAttempted(true)
    }, [])

    const gatedSteps = useMemo(
        () =>
            submitAttempted
                ? steps
                : steps.map((step) =>
                      touched.has(step.id) ? step : { ...step, invalid: false }
                  ),
        [steps, submitAttempted, touched]
    )

    return { steps: gatedSteps, onSelectStep, onSubmitAttempt }
}
