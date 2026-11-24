export function createQueryKeys(scope: string) {
    const base = [scope] as const
    return {
        all: base,
        lists: () => [...base, 'list'] as const,
        details: () => [...base, 'detail'] as const,
        detail: (id: string) => [...base, 'detail', id] as const,
        filtered: (filters?: Record<string, unknown>) =>
            [...base, 'list', { filters }] as const,
    }
}
