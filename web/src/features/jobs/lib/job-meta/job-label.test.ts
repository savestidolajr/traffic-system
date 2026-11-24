import { describe, it, expect } from 'vitest'
import { isSameLabel } from './job-label'

describe('isSameLabel', () => {
    it('matches identical strings', () => {
        expect(isSameLabel('Sydney', 'Sydney')).toBe(true)
    })

    it('is case-insensitive', () => {
        expect(isSameLabel('sydney', 'Sydney')).toBe(true)
        expect(
            isSameLabel('sydney, nsw, australia', 'Sydney, NSW, Australia')
        ).toBe(true)
    })

    it('ignores surrounding whitespace', () => {
        expect(isSameLabel('  Sydney  ', 'Sydney')).toBe(true)
    })

    it('returns false for genuinely different labels', () => {
        expect(isSameLabel('Sydney', 'Melbourne')).toBe(false)
        expect(isSameLabel('3AW 693 Melbourne Current', 'Melbourne')).toBe(
            false
        )
    })
})
