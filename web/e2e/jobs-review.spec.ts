import { test, expect } from './fixtures'
import { JobsPage } from './pages/JobsPage'
import { resetMockStore } from './pages/reset'

const fixtureJobs = [
    'arn-job-00007',
    'arn-job-00008',
    'arn-job-00005',
    'arn-job-00009',
    'arn-job-00010',
    'arn-job-00013',
    'arn-job-00012',
]

test.beforeEach(async ({ page }) => {
    await resetMockStore(page)
})

test('all fixture job states render review surfaces', async ({ page }) => {
    const jobs = new JobsPage(page)
    for (const jobId of fixtureJobs) {
        await jobs.openJob(jobId)
        await expect(page.locator('body')).toContainText(
            /script|audio|traffic|failed|fallback|approved|review/i
        )
        await expect(page.locator('body')).not.toContainText(
            /application error/i
        )
    }
})

test('waiting approval job supports approve and revise actions', async ({
    page,
}) => {
    const jobs = new JobsPage(page)
    await jobs.openJob('arn-job-00007')
    await expect(page.locator('body')).toContainText(/evidence|audio|approve/i)
    await jobs.approve()
    await expect(page.locator('body')).toContainText(/approved/i)

    await resetMockStore(page)
    await jobs.openJob('arn-job-00007')
    await jobs.revise()
    await expect(page.locator('body')).toContainText(/feedback|revise|reject/i)
})

test('adjust, failed, and terminal states expose correct controls', async ({
    page,
}) => {
    const jobs = new JobsPage(page)
    await jobs.openJob('arn-job-00008')
    await jobs.switchTab('Adjust')
    await expect(page.locator('body')).toContainText(
        /include|exclude|corridor|incident|flow/i
    )

    await jobs.openJob('arn-job-00013')
    await expect(page.locator('body')).toContainText(/failed|retry|regenerate/i)

    await jobs.openJob('arn-job-00009')
    await expect(page.locator('body')).toContainText(/approved|publish/i)

    await jobs.openJob('arn-job-00012')
    await expect(page.locator('body')).toContainText(/fallback/i)
})

test('rundown table: incidents and good runs ranked in one table', async ({
    page,
}) => {
    const jobs = new JobsPage(page)
    await jobs.openJob('arn-job-00008')
    await jobs.switchTab('Adjust')

    // Mobile keeps the decision-bearing road, severity, and score fields
    // without horizontal scroll. Type/Class rejoin only from 2xl (1536px),
    // once the card is wide enough that they cannot starve the road name —
    // so they are hidden on BOTH configured viewports (1440 / 390).
    const classHeader = page.getByText('Class', { exact: true })
    const width = page.viewportSize()?.width ?? 1440
    const isMobile = width < 768
    if (width >= 1536) {
        await expect(classHeader).toBeVisible()
    } else {
        await expect(classHeader).toBeHidden()
    }
    if (isMobile) {
        await expect
            .poll(() =>
                page.evaluate(
                    () =>
                        document.documentElement.scrollWidth <=
                        document.documentElement.clientWidth
                )
            )
            .toBe(true)
    }
    await expect(page.getByText('Severity', { exact: true })).toBeVisible()
    await expect(page.getByText('Score', { exact: true })).toBeVisible()
    await expect(page.getByText(/sent to writer/i).first()).toBeVisible()
    await expect(page.getByText(/not sent to writer/i).first()).toBeVisible()

    // Flowing good-runs are scored in the SAME ranked table (damped below
    // incidents), and roads are dragged in/out — no per-road checkbox. The
    // "Good run" type cell shares the Type column's 2xl gate; the clearness
    // cell ("Clear" etc.) is always present.
    const goodRunLabel = page.getByText('Good run', { exact: true }).first()
    if (width >= 1536) {
        await expect(goodRunLabel).toBeVisible()
    } else {
        await expect(goodRunLabel).toBeHidden()
        await expect(
            page.getByText('Clear', { exact: true }).first()
        ).toBeVisible()
    }
    await expect(
        page.getByRole('checkbox', { name: /take .* off air/i })
    ).toHaveCount(0)
    // The drag handle doubles as a click toggle; its label names the action
    // ("Add/Remove {road} to/from the bulletin"), not the drag mechanics.
    await expect(
        page
            .getByRole('button', {
                name: /^(Add|Remove) .+ (to|from) the bulletin$/i,
            })
            .first()
    ).toBeVisible()
})

test('script editor: live counter recolors by slot fit and re-voices verbatim', async ({
    page,
}) => {
    const jobs = new JobsPage(page)
    await jobs.openJob('arn-job-00007')

    // Enter the verbatim re-voice editor.
    await page.getByRole('button', { name: 'Edit', exact: true }).click()
    const textarea = page.getByRole('textbox', { name: /edit script/i })
    await expect(textarea).toBeVisible()

    // Unchanged draft → Re-voice is disabled (no-op guard).
    const revoice = page.getByRole('button', { name: /re-voice/i })
    await expect(revoice).toBeDisabled()

    // Below the band: a short read can't fill the 30s slot → warning copy.
    await textarea.fill(Array(5).fill('traffic').join(' '))
    await expect(page.getByText(/short for 30s/i)).toBeVisible()
    await expect(revoice).toBeEnabled()

    // In the band: fits the slot → success copy.
    await textarea.fill(Array(80).fill('traffic').join(' '))
    await expect(page.getByText(/fits the 30s slot/i)).toBeVisible()

    // Above the band: a long read is sped up to fit → warning copy.
    await textarea.fill(Array(120).fill('traffic').join(' '))
    await expect(page.getByText(/long for 30s/i)).toBeVisible()

    // Re-voice commits the operator's exact text verbatim (no AI rewrite); the
    // mock applies editedScript as-is and returns the job to WAITING_APPROVAL.
    await textarea.fill(
        'Operator re-voiced bulletin with the Zanzibar checkpoint marker.'
    )
    await revoice.click()
    await expect(textarea).toBeHidden()
    await expect(page.locator('body')).toContainText(/Zanzibar/i)
})

test('revise dialog no longer offers a separate re-voice-only action', async ({
    page,
}) => {
    const jobs = new JobsPage(page)
    await jobs.openJob('arn-job-00007')
    await jobs.revise()
    // The script edit now routes through the inline Re-voice button, so the
    // dialog's old "Re-voice only" shortcut and the "Edited script" chip are gone.
    await expect(
        page.getByRole('button', { name: /re-voice only/i })
    ).toHaveCount(0)
    await expect(page.getByText(/edited script/i)).toHaveCount(0)
})
