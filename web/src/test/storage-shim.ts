// Node 25+ ships an experimental built-in `localStorage` global that is
// `undefined` unless the process was started with `--localstorage-file`. Vitest
// copies Node's globals onto the jsdom window, so that undefined value shadows
// jsdom's own Storage and every test touching `window.localStorage` dies with
// "Cannot read properties of undefined (reading 'clear')".
//
// CI pins Node 22.x and never sees it; a developer on a newer Node loses ~100
// tests at once, which reads like a code regression and is not one. This shim
// restores a working Storage only when the host has shadowed it, so the suite
// does not depend on which Node happens to be installed.
class MemoryStorage implements Storage {
    private entries = new Map<string, string>()

    get length(): number {
        return this.entries.size
    }

    clear(): void {
        this.entries.clear()
    }

    getItem(key: string): string | null {
        return this.entries.get(String(key)) ?? null
    }

    key(index: number): string | null {
        return [...this.entries.keys()][index] ?? null
    }

    removeItem(key: string): void {
        this.entries.delete(String(key))
    }

    setItem(key: string, value: string): void {
        this.entries.set(String(key), String(value))
    }
}

function ensureStorage(name: 'localStorage' | 'sessionStorage') {
    // Only intervene when the host shadowed it. A real jsdom Storage must win,
    // so a genuine breakage in jsdom stays visible instead of being papered
    // over by this shim.
    if (typeof window === 'undefined') return
    if (
        window[name] != null &&
        typeof (window[name] as Storage).clear === 'function'
    )
        return
    Object.defineProperty(window, name, {
        value: new MemoryStorage(),
        configurable: true,
        writable: false,
    })
    Object.defineProperty(globalThis, name, {
        value: window[name],
        configurable: true,
        writable: false,
    })
}

if (
    typeof window !== 'undefined' &&
    typeof window.ResizeObserver !== 'function'
) {
    class ResizeObserverStub {
        observe() {}
        unobserve() {}
        disconnect() {}
    }
    window.ResizeObserver =
        ResizeObserverStub as unknown as typeof ResizeObserver
    globalThis.ResizeObserver =
        ResizeObserverStub as unknown as typeof ResizeObserver
}
ensureStorage('localStorage')
ensureStorage('sessionStorage')
