/**
 * jsdom shims for Radix UI primitives that drive pointer capture.
 *
 * Radix's Select (and Slider, Dropdown) call `hasPointerCapture` /
 * `setPointerCapture` / `scrollIntoView` on the trigger while opening. jsdom
 * implements none of them, so a `userEvent.click` on a `combobox` throws
 * `TypeError: target.hasPointerCapture is not a function` — an uncaught
 * exception that fails the whole test FILE, not just the assertion.
 *
 * Import this module for its side effects in any jsdom test that opens a Radix
 * Select:
 *
 *     import '@/test/radix-jsdom'
 *
 * Only missing methods are installed, so a future jsdom that ships them wins.
 */
const proto = Element.prototype as unknown as Record<string, unknown>

if (typeof proto.hasPointerCapture !== 'function') {
    proto.hasPointerCapture = () => false
}
if (typeof proto.setPointerCapture !== 'function') {
    proto.setPointerCapture = () => {}
}
if (typeof proto.releasePointerCapture !== 'function') {
    proto.releasePointerCapture = () => {}
}
if (typeof proto.scrollIntoView !== 'function') {
    proto.scrollIntoView = () => {}
}
