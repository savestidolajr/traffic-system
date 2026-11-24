/**
 * True when two location labels refer to the same place — case- and
 * whitespace-insensitive. Used to collapse a job's Station/Market display
 * to a single line when an ad-hoc job's station name IS the market (e.g.
 * "Sydney" / "Sydney, NSW, Australia" normalized inputs that came out equal).
 */
export function isSameLabel(a: string, b: string): boolean {
    return a.trim().toLowerCase() === b.trim().toLowerCase()
}
