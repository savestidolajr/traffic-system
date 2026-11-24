// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { VoiceVariantCard } from './VoiceVariantCard'
import type { VoiceProfile } from '../profile-types'
import { TooltipProvider } from '@/components/ui/tooltip'

// ---------------------------------------------------------------------------
// Mocks
// ---------------------------------------------------------------------------

vi.mock('@/lib/formatting', () => ({
    formatRelativeTime: vi.fn((iso: string) => `rel:${iso}`),
    formatDateTime: vi.fn((iso: string) => `abs:${iso}`),
}))

vi.mock('./SamplePlayButton', () => ({
    SamplePlayButton: ({ label, url }: { label?: string; url: string }) => (
        <button
            type="button"
            aria-label={label ?? 'Play sample'}
            data-url={url}
        >
            ▶
        </button>
    ),
}))

// Silence Radix UI / React 19 noise in jsdom
beforeEach(() => {
    vi.restoreAllMocks()
    Element.prototype.getAnimations ??= () => []
})

// ---------------------------------------------------------------------------
// Helpers — raw createRoot + act + DOM queries (no @testing-library/react)
// ---------------------------------------------------------------------------

let root: Root | null = null

async function render(ui: React.ReactElement) {
    const container = document.createElement('div')
    document.body.appendChild(container)
    root = createRoot(container)
    await act(async () => {
        root!.render(<TooltipProvider>{ui}</TooltipProvider>)
    })
    return container
}

function cleanup() {
    if (root) {
        act(() => {
            root!.unmount()
        })
    }
    root = null
    document.body.innerHTML = ''
}

afterEach(cleanup)

/**
 * Radix DropdownMenuTrigger listens on pointerdown/pointerup.  `.click()`
 * only fires mousedown→mouseup→click, so we dispatch pointer events too.
 */
function openDropdown(trigger: HTMLElement) {
    trigger.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }))
    trigger.dispatchEvent(new PointerEvent('pointerup', { bubbles: true }))
    trigger.click()
}

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

function makeVersion(
    overrides: Record<string, unknown> = {}
): Record<string, unknown> {
    return {
        version: 1,
        settings: {
            speed: 1.0,
            stability: 0.5,
            similarityBoost: 0.8,
            style: 0.0,
            useSpeakerBoost: false,
        },
        model: 'eleven_v3',
        changeNote: null,
        createdAt: '2025-06-01T00:00:00Z',
        createdBy: null,
        sampleUrl: null,
        configurationKey: 'vb1_1.00_0.50_0.80_0.00_false',
        calibration: null,
        runSamples: [],
        paceBoostWpm: 0,
        ...overrides,
    }
}

function makeProfile(
    versionOverrides: Record<string, unknown> = {},
    profileOverrides: Record<string, unknown> = {}
): VoiceProfile {
    return {
        id: 'profile-1',
        name: 'Test Voice',
        description: null,
        baseVoiceId: 'base-voice-1',
        baseVoiceName: 'ARN Hudson',
        provider: 'elevenlabs',
        status: 'active',
        currentVersion: 1,
        createdAt: '2025-06-01T00:00:00Z',
        updatedAt: '2025-06-15T12:00:00Z',
        createdBy: null,
        assignedStations: [],
        versions: [makeVersion(versionOverrides)],
        ...profileOverrides,
    } as unknown as VoiceProfile
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('VoiceVariantCard', () => {
    // assignedStations is a required prop (the card derives it from schedules;
    // see lib/assignments.ts) — the fixture defaults to none assigned and the
    // assignment-footer tests below override it with real station labels.
    const defaultProps = {
        assignedStations: [] as readonly string[],
        onView: vi.fn(),
        onEdit: vi.fn(),
        onDuplicate: vi.fn(),
        onDelete: vi.fn(),
        onArchive: vi.fn(),
        onUnarchive: vi.fn(),
    }

    // ---- 1. WPM display ------------------------------------------------

    describe('wpm display', () => {
        it('shows collapsed empty-state when calibration is null', async () => {
            const container = await render(
                <VoiceVariantCard profile={makeProfile()} {...defaultProps} />
            )
            // When no data, stats strip collapses to "Not aired yet"
            const text = container.textContent ?? ''
            expect(text).toContain('Not aired yet')
            expect(text).not.toContain('wpm')
        })

        it('shows rounded WPM once the voice has aired', async () => {
            const container = await render(
                <VoiceVariantCard
                    profile={makeProfile({
                        calibration: {
                            wordsPerMinute: 165,
                            syllablesPerSecond: 4.5,
                            syllablesPerWord: 1.8,
                            runCount: 2,
                            calibratedAt: '2025-06-10T08:00:00Z',
                        },
                    })}
                    {...defaultProps}
                />
            )
            expect(container.textContent).toContain('165')
            expect(container.textContent).toContain('wpm')
            // No em dash when WPM is present
            expect(container.textContent).not.toMatch(/—/)
        })

        it('shows word-target caption once the voice has aired', async () => {
            // wordTargetForDuration(165, 30) = Math.round((165/60)*30) = 83
            const container = await render(
                <VoiceVariantCard
                    profile={makeProfile({
                        calibration: {
                            wordsPerMinute: 165,
                            syllablesPerSecond: 4.5,
                            syllablesPerWord: 1.8,
                            runCount: 2,
                            calibratedAt: '2025-06-10T08:00:00Z',
                        },
                    })}
                    {...defaultProps}
                />
            )
            expect(container.textContent).toContain('~83/30s')
        })
    })

    // ---- 2. Pace boost badge -------------------------------------------

    describe('pace boost badge', () => {
        it('renders +N badge when paceBoostWpm > 0', async () => {
            const container = await render(
                <VoiceVariantCard
                    profile={makeProfile({
                        paceBoostWpm: 18,
                        calibration: {
                            wordsPerMinute: 160,
                            syllablesPerSecond: 4.2,
                            syllablesPerWord: 1.8,
                            runCount: 2,
                            calibratedAt: '2025-06-10T08:00:00Z',
                        },
                    })}
                    {...defaultProps}
                />
            )
            expect(container.textContent).toContain('+18')
        })

        it('does not render pace boost badge when paceBoostWpm is 0', async () => {
            const container = await render(
                <VoiceVariantCard
                    profile={makeProfile({
                        paceBoostWpm: 0,
                        calibration: {
                            wordsPerMinute: 160,
                            syllablesPerSecond: 4.2,
                            syllablesPerWord: 1.8,
                            runCount: 2,
                            calibratedAt: '2025-06-10T08:00:00Z',
                        },
                    })}
                    {...defaultProps}
                />
            )
            expect(container.textContent).not.toContain('+0')
        })
    })

    // ---- 3. Airings stat ------------------------------------------------

    describe('airings stat', () => {
        it('shows collapsed empty-state when calibration is null', async () => {
            const container = await render(
                <VoiceVariantCard profile={makeProfile()} {...defaultProps} />
            )
            const text = container.textContent ?? ''
            expect(text).toContain('Not aired yet')
            expect(text).not.toContain('airings')
        })

        it('shows singular "airing" when runCount is 1', async () => {
            const container = await render(
                <VoiceVariantCard
                    profile={makeProfile({
                        calibration: {
                            wordsPerMinute: 160,
                            syllablesPerSecond: 4.2,
                            syllablesPerWord: 1.8,
                            runCount: 1,
                            calibratedAt: '2025-06-10T08:00:00Z',
                        },
                    })}
                    {...defaultProps}
                />
            )
            const text = container.textContent ?? ''
            expect(text).toContain('1')
            expect(text).toContain('airing')
            // Must NOT contain plural "airings" when count is 1
            expect(text).not.toMatch(/airings/)
        })

        it('shows plural "airings" when runCount is > 1', async () => {
            const container = await render(
                <VoiceVariantCard
                    profile={makeProfile({
                        calibration: {
                            wordsPerMinute: 160,
                            syllablesPerSecond: 4.2,
                            syllablesPerWord: 1.8,
                            runCount: 5,
                            calibratedAt: '2025-06-10T08:00:00Z',
                        },
                    })}
                    {...defaultProps}
                />
            )
            const text = container.textContent ?? ''
            expect(text).toContain('5')
            expect(text).toContain('airings')
        })

        it('shows formatRelativeTime in Proven caption when aired and calibratedAt present', async () => {
            const { formatRelativeTime } = await import('@/lib/formatting')
            const container = await render(
                <VoiceVariantCard
                    profile={makeProfile({
                        calibration: {
                            wordsPerMinute: 160,
                            syllablesPerSecond: 4.2,
                            syllablesPerWord: 1.8,
                            runCount: 3,
                            calibratedAt: '2025-06-10T08:00:00Z',
                        },
                    })}
                    {...defaultProps}
                />
            )
            expect(formatRelativeTime).toHaveBeenCalledWith(
                '2025-06-10T08:00:00Z'
            )
            expect(container.textContent).toContain('rel:2025-06-10T08:00:00Z')
        })

        it('collapses to "Not aired yet" when runCount is 0', async () => {
            const container = await render(
                <VoiceVariantCard
                    profile={makeProfile({
                        calibration: {
                            wordsPerMinute: 160,
                            syllablesPerSecond: 4.2,
                            syllablesPerWord: 1.8,
                            runCount: 0,
                            calibratedAt: null,
                        },
                    })}
                    {...defaultProps}
                />
            )
            // Never aired → the stats band collapses to the single body; the
            // zeroed meters must not render at all.
            expect(container.textContent).toContain('Not aired yet')
            expect(container.textContent).not.toContain('airings')
        })

        // Placeholder is a plain hyphen since the 2026-09-02 copy sweep (rendered em
        // dashes were removed app-wide; absent-value markers became '-').
        it('shows a hyphen placeholder when aired but the calibration timestamp is missing', async () => {
            const container = await render(
                <VoiceVariantCard
                    profile={makeProfile({
                        calibration: {
                            wordsPerMinute: 160,
                            syllablesPerSecond: 4.2,
                            syllablesPerWord: 1.8,
                            runCount: 1,
                            calibratedAt: null,
                        },
                    })}
                    {...defaultProps}
                />
            )
            // Aired (runCount > 0) but no calibration time → the Proven
            // caption has no date to show, and it is NOT "not aired yet".
            expect(container.textContent).toContain('Proven-')
            expect(container.textContent).not.toContain('Not aired yet')
            expect(container.textContent).not.toContain('not aired yet')
        })
    })

    // ---- 4. Assignment footer -------------------------------------------
    // The card derives assignment from the schedules list (the wire DTO's
    // `assignedStations` is never populated), so the count arrives as a prop.

    describe('assignment footer', () => {
        it('shows "Unassigned" when assignedStations is empty', async () => {
            const container = await render(
                <VoiceVariantCard profile={makeProfile()} {...defaultProps} />
            )
            expect(container.textContent).toContain('Unassigned')
        })

        it('has "Not assigned to any station" title when unassigned', async () => {
            const container = await render(
                <VoiceVariantCard profile={makeProfile()} {...defaultProps} />
            )
            const spans = container.querySelectorAll('span[title]')
            const unassignedSpan = Array.from(spans).find(
                (s) => s.getAttribute('title') === 'Not assigned to any station'
            )
            expect(unassignedSpan).toBeDefined()
        })

        it('shows singular "1 station" when assigned to one station', async () => {
            const container = await render(
                <VoiceVariantCard
                    profile={makeProfile()}
                    {...defaultProps}
                    assignedStations={['2DAY']}
                />
            )
            const text = container.textContent ?? ''
            expect(text).toContain('1 station')
            // Must not contain "1 stations"
            expect(text).not.toMatch(/1 stations/)
        })

        it('shows "On air at: ..." title when assigned', async () => {
            const container = await render(
                <VoiceVariantCard
                    profile={makeProfile()}
                    {...defaultProps}
                    assignedStations={['2DAY', 'NOVA']}
                />
            )
            const spans = container.querySelectorAll('span[title]')
            const assignedSpan = Array.from(spans).find(
                (s) => s.getAttribute('title') === 'On air at: 2DAY, NOVA'
            )
            expect(assignedSpan).toBeDefined()
        })

        it('shows plural "2 stations" when assigned to two stations', async () => {
            const container = await render(
                <VoiceVariantCard
                    profile={makeProfile()}
                    {...defaultProps}
                    assignedStations={['2DAY', 'NOVA']}
                />
            )
            expect(container.textContent).toContain('2 stations')
        })
    })

    // ---- 5. Archived badge ----------------------------------------------

    describe('archived badge', () => {
        it('renders "Archived" badge when status is archived', async () => {
            const container = await render(
                <VoiceVariantCard
                    profile={makeProfile({}, { status: 'archived' })}
                    {...defaultProps}
                />
            )
            expect(container.textContent).toContain('Archived')
        })

        it('does not render "Archived" badge when status is active', async () => {
            const container = await render(
                <VoiceVariantCard
                    profile={makeProfile({}, { status: 'active' })}
                    {...defaultProps}
                />
            )
            expect(container.textContent).not.toMatch(/Archived/)
        })
    })

    // ---- 6. Truncation tooltips -----------------------------------------

    describe('truncation tooltips', () => {
        it('has title attribute equal to full profile name', async () => {
            const container = await render(
                <VoiceVariantCard
                    profile={makeProfile(
                        {},
                        { name: 'A Very Long Voice Name' }
                    )}
                    {...defaultProps}
                />
            )
            // h2, not h3: the gallery pages put these cards directly under the
            // page <h1>, and an H1→H3 skip is a `heading-order` violation the
            // Lighthouse accessibility gate fails on.
            const heading = container.querySelector('h2')
            expect(heading).not.toBeNull()
            expect(heading!.getAttribute('title')).toBe(
                'A Very Long Voice Name'
            )
        })

        it('has title attribute equal to full baseVoiceName', async () => {
            const container = await render(
                <VoiceVariantCard
                    profile={makeProfile(
                        {},
                        { baseVoiceName: 'ARN Hudson with a Long Qualifier' }
                    )}
                    {...defaultProps}
                />
            )
            const subtitle = container.querySelector('p')
            expect(subtitle).not.toBeNull()
            expect(subtitle!.getAttribute('title')).toBe(
                'ARN Hudson with a Long Qualifier'
            )
        })
    })

    // ---- 7. Sample play button vs static mic icon -----------------------

    describe('sample play button vs static icon', () => {
        it('renders SamplePlayButton when sampleUrl is set', async () => {
            const container = await render(
                <VoiceVariantCard
                    profile={makeProfile({
                        sampleUrl: 'https://example.com/sample.mp3',
                    })}
                    {...defaultProps}
                />
            )
            const btn = container.querySelector(
                'button[aria-label="Play latest sample"]'
            )
            expect(btn).not.toBeNull()
        })

        it('does not render SamplePlayButton when sampleUrl is null', async () => {
            const container = await render(
                <VoiceVariantCard
                    profile={makeProfile({ sampleUrl: null })}
                    {...defaultProps}
                />
            )
            const btn = container.querySelector(
                'button[aria-label="Play latest sample"]'
            )
            expect(btn).toBeNull()
        })
    })

    // ---- 8. Interaction callbacks ---------------------------------------

    describe('interaction callbacks', () => {
        it('calls onEdit with profile.id when Edit is clicked', async () => {
            const onEdit = vi.fn()
            const profile = makeProfile()
            const container = await render(
                <VoiceVariantCard
                    profile={profile}
                    assignedStations={[]}
                    onView={vi.fn()}
                    onEdit={onEdit}
                    onDuplicate={vi.fn()}
                />
            )
            const trigger = container.querySelector(
                'button[aria-label="Variant actions"]'
            ) as HTMLButtonElement
            await act(async () => {
                openDropdown(trigger)
            })

            // Radix portals menu items to body — find Edit
            const editItem = Array.from(
                document.body.querySelectorAll('[role="menuitem"]')
            ).find((el) => el.textContent?.trim().includes('Edit'))
            expect(editItem).toBeDefined()
            await act(async () => {
                ;(editItem as HTMLElement).click()
            })

            expect(onEdit).toHaveBeenCalledWith('profile-1')
        })

        it('calls onDuplicate with profile when Duplicate is clicked', async () => {
            const onDuplicate = vi.fn()
            const profile = makeProfile()
            const container = await render(
                <VoiceVariantCard
                    profile={profile}
                    assignedStations={[]}
                    onView={vi.fn()}
                    onEdit={vi.fn()}
                    onDuplicate={onDuplicate}
                />
            )
            const trigger = container.querySelector(
                'button[aria-label="Variant actions"]'
            ) as HTMLButtonElement
            await act(async () => {
                openDropdown(trigger)
            })

            // Radix portals menu items to body — find Duplicate
            const dupItem = Array.from(
                document.body.querySelectorAll('[role="menuitem"]')
            ).find((el) => el.textContent?.trim().includes('Duplicate'))
            expect(dupItem).toBeDefined()
            await act(async () => {
                ;(dupItem as HTMLElement).click()
            })

            expect(onDuplicate).toHaveBeenCalledWith(
                expect.objectContaining({ id: 'profile-1' })
            )
        })
    })

    // ---- 9. Role gating ------------------------------------------------
    // Mirrors the bookends grant (f28ba062): every voice-profile route except
    // DELETE resolves to Operator in RouteRoleMatrix, so an operator gets the
    // full actions menu — View, Edit, Duplicate, Archive — and Delete alone
    // stays Admin because it is unrecoverable and archive is the reversible
    // retire path.

    describe('role gating', () => {
        function menuLabels(): readonly string[] {
            return [...document.body.querySelectorAll('[role="menuitem"]')].map(
                (item) => (item.textContent ?? '').trim()
            )
        }

        it('offers an operator the editing actions but not Delete', async () => {
            const onDelete = vi.fn()
            const container = await render(
                <VoiceVariantCard
                    profile={makeProfile()}
                    assignedStations={['KIIS 1065']}
                    canDelete={false}
                    {...defaultProps}
                    onDelete={onDelete}
                />
            )

            const trigger = container.querySelector<HTMLElement>(
                'button[aria-label="Variant actions"]'
            )
            expect(trigger).not.toBeNull()
            await act(async () => openDropdown(trigger!))

            const labels = menuLabels()
            expect(labels).toContain('Edit')
            expect(labels).toContain('Duplicate')
            expect(labels).toContain('Archive')
            expect(labels).not.toContain('Delete')
        })

        it('offers Delete to an admin', async () => {
            const onDelete = vi.fn()
            const container = await render(
                <VoiceVariantCard
                    profile={makeProfile()}
                    assignedStations={[]}
                    onDelete={onDelete}
                    {...defaultProps}
                />
            )

            const trigger = container.querySelector<HTMLElement>(
                'button[aria-label="Variant actions"]'
            )
            expect(trigger).not.toBeNull()
            await act(async () => openDropdown(trigger!))

            expect(menuLabels()).toContain('Delete')
        })

        it('withholds Delete in the table view too', async () => {
            const { VoiceProfileTable } = await import('./VoiceProfileTable')
            const container = await render(
                <VoiceProfileTable
                    profiles={[makeProfile()]}
                    assignmentsByProfileId={new Map()}
                    canDelete={false}
                    onView={vi.fn()}
                    onEdit={vi.fn()}
                    onDuplicate={vi.fn()}
                    onDelete={vi.fn()}
                    onArchive={vi.fn()}
                    onUnarchive={vi.fn()}
                />
            )

            const trigger = container.querySelector<HTMLElement>(
                'button[aria-label="Actions for Test Voice"]'
            )
            expect(trigger).not.toBeNull()
            await act(async () => openDropdown(trigger!))

            const labels = menuLabels()
            expect(labels).toContain('Edit')
            expect(labels).not.toContain('Delete')
        })
    })
})
