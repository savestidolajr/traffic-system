// @vitest-environment jsdom
import '@testing-library/jest-dom/vitest'
import '@/test/radix-jsdom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { RuleRow } from './RuleRow'

afterEach(() => {
    cleanup()
})

describe('RuleRow actions', () => {
    it('exposes edit from the kebab menu and confirms deletion', async () => {
        const user = userEvent.setup()
        const onEdit = vi.fn()
        const onDelete = vi.fn()

        render(
            <div className="group">
                <RuleRow
                    enabled
                    onToggle={vi.fn()}
                    label="Keep it factual"
                    itemLabel="guidance"
                    onEdit={onEdit}
                    editLabel="Edit guidance"
                    onDelete={onDelete}
                    deleteLabel="Delete guidance"
                />
            </div>
        )

        await user.click(
            screen.getByRole('button', { name: 'guidance actions' })
        )
        await user.click(
            screen.getByRole('menuitem', { name: 'Edit guidance' })
        )
        expect(onEdit).toHaveBeenCalledOnce()

        await user.click(
            screen.getByRole('button', { name: 'guidance actions' })
        )
        await user.click(
            screen.getByRole('menuitem', { name: 'Delete guidance' })
        )
        expect(onDelete).not.toHaveBeenCalled()
        expect(screen.getByRole('alertdialog')).toHaveTextContent(
            'Delete guidance?'
        )

        await user.click(screen.getByRole('button', { name: 'Delete' }))
        expect(onDelete).toHaveBeenCalledOnce()
    })

    it('keeps the row action menu out of read-only rows', () => {
        render(
            <RuleRow
                enabled
                onToggle={vi.fn()}
                label="Protected rule"
                onDelete={vi.fn()}
                readOnly
            />
        )

        expect(
            screen.queryByRole('button', { name: 'Row actions' })
        ).not.toBeInTheDocument()
    })
})
