# UI inventory

**Generated — do not edit.** Regenerate with `npm run ui:inventory`; `scripts/verify.sh` fails when this file is stale.

The vocabulary this app already speaks. Read it before building a new component: if something here covers the job, use it. Companion to `docs/DESIGN-SYSTEM.md` (icons, type, colour) — that one governs *how things look*, this one governs *what exists*.

Columns: **Requires** is the props a caller MUST pass (`children`/`ref`/`key` elided). **or** marks a props union — satisfy exactly one branch. **Variants** are the cva options declared in the same file. **Used** counts distinct files that render the component.

## Primitives — `src/components/ui/`

shadcn/Radix-derived building blocks. Restyle these; never fork one into a feature folder.

| Component | Requires | Variants | Used | What it is |
|---|---|---|---|---|
| **Alert**<br><sub>ui/alert.tsx</sub> | — | variant: default/destructive | 3 |  |
| **AlertDescription**<br><sub>ui/alert.tsx</sub> | — | — | 3 |  |
| **AlertDialog**<br><sub>ui/alert-dialog.tsx</sub> | — | — | 3 |  |
| **AlertDialogAction**<br><sub>ui/alert-dialog.tsx</sub> | — | — | 3 |  |
| **AlertDialogCancel**<br><sub>ui/alert-dialog.tsx</sub> | — | — | 3 |  |
| **AlertDialogContent**<br><sub>ui/alert-dialog.tsx</sub> | — | — | 3 |  |
| **AlertDialogDescription**<br><sub>ui/alert-dialog.tsx</sub> | — | — | 3 |  |
| **AlertDialogFooter**<br><sub>ui/alert-dialog.tsx</sub> | — | — | 3 |  |
| **AlertDialogHeader**<br><sub>ui/alert-dialog.tsx</sub> | — | — | 3 |  |
| **AlertDialogOverlay**<br><sub>ui/alert-dialog.tsx</sub> | — | — | 0 |  |
| **AlertDialogPortal**<br><sub>ui/alert-dialog.tsx</sub> | — | — | 0 |  |
| **AlertDialogTitle**<br><sub>ui/alert-dialog.tsx</sub> | — | — | 3 |  |
| **AlertDialogTrigger**<br><sub>ui/alert-dialog.tsx</sub> | — | — | 0 |  |
| **AlertTitle**<br><sub>ui/alert.tsx</sub> | — | — | 3 |  |
| **AuroraHero**<br><sub>ui/aceternity/aurora-hero.tsx</sub> | — | — | 2 | Aceternity "Aurora Background", adapted for the workstation: a contained hero band (not a 100vh marketing section), the palette shifted from Aceternity's blues to the ARN purple family, and the drift wrapped in `motion-safe` (reduced motion gets the same aurora, frozen). |
| **AuroraWash**<br><sub>ui/aceternity/aurora-wash.tsx</sub> | `gradient` | — | 1 | Aceternity's Aurora Background at micro scale: a corner-masked gradient bloom for a single data block, tinted by the data it sits behind (e.g. |
| **Badge**<br><sub>ui/badge.tsx</sub> | — | variant: default/secondary/destructive/rejected/outline/success/warning/muted/approved/requested/hereFetched/incidents/scriptReady/audioReady/synthesizingAudio/fallback/info/broadcasting/scheduled | 41 |  |
| **Button**<br><sub>ui/button.tsx</sub> | — | variant: default/destructive/outline/secondary/ghost/link<br>size: default/sm/lg/icon | 80 |  |
| **Card**<br><sub>ui/card.tsx</sub> | — | — | 2 |  |
| **CardAction**<br><sub>ui/card.tsx</sub> | — | — | 0 |  |
| **CardContent**<br><sub>ui/card.tsx</sub> | — | — | 2 |  |
| **CardDescription**<br><sub>ui/card.tsx</sub> | — | — | 1 |  |
| **CardFooter**<br><sub>ui/card.tsx</sub> | — | — | 0 |  |
| **CardHeader**<br><sub>ui/card.tsx</sub> | — | — | 1 |  |
| **CardTitle**<br><sub>ui/card.tsx</sub> | — | — | 1 |  |
| **Command**<br><sub>ui/command.tsx</sub> | — | — | 0 |  |
| **CommandDialog**<br><sub>ui/command.tsx</sub> | — | — | 1 |  |
| **CommandEmpty**<br><sub>ui/command.tsx</sub> | — | — | 1 |  |
| **CommandGroup**<br><sub>ui/command.tsx</sub> | — | — | 1 |  |
| **CommandInput**<br><sub>ui/command.tsx</sub> | — | — | 1 |  |
| **CommandItem**<br><sub>ui/command.tsx</sub> | — | — | 1 |  |
| **CommandList**<br><sub>ui/command.tsx</sub> | — | — | 1 |  |
| **CommandSeparator**<br><sub>ui/command.tsx</sub> | — | — | 0 |  |
| **CommandShortcut**<br><sub>ui/command.tsx</sub> | — | — | 1 |  |
| **Container**<br><sub>ui/container.tsx</sub> | — | — | 1 |  |
| **Dialog**<br><sub>ui/dialog.tsx</sub> | — | — | 6 |  |
| **DialogClose**<br><sub>ui/dialog.tsx</sub> | — | — | 1 |  |
| **DialogContent**<br><sub>ui/dialog.tsx</sub> | — | — | 5 |  |
| **DialogDescription**<br><sub>ui/dialog.tsx</sub> | — | — | 4 |  |
| **DialogFooter**<br><sub>ui/dialog.tsx</sub> | — | — | 2 |  |
| **DialogHeader**<br><sub>ui/dialog.tsx</sub> | — | — | 4 |  |
| **DialogOverlay**<br><sub>ui/dialog.tsx</sub> | — | — | 1 |  |
| **DialogPortal**<br><sub>ui/dialog.tsx</sub> | — | — | 1 |  |
| **DialogTitle**<br><sub>ui/dialog.tsx</sub> | — | — | 6 |  |
| **DialogTrigger**<br><sub>ui/dialog.tsx</sub> | — | — | 0 |  |
| **DotGridBackdrop**<br><sub>ui/aceternity/dot-grid-backdrop.tsx</sub> | — | — | 3 | Aceternity-style dot-grid backdrop, radially masked so it fades toward the edges. |
| **DropdownMenu**<br><sub>ui/dropdown-menu.tsx</sub> | — | — | 13 |  |
| **DropdownMenuCheckboxItem**<br><sub>ui/dropdown-menu.tsx</sub> | — | — | 0 |  |
| **DropdownMenuContent**<br><sub>ui/dropdown-menu.tsx</sub> | — | — | 13 |  |
| **DropdownMenuGroup**<br><sub>ui/dropdown-menu.tsx</sub> | — | — | 6 |  |
| **DropdownMenuItem**<br><sub>ui/dropdown-menu.tsx</sub> | — | — | 13 |  |
| **DropdownMenuLabel**<br><sub>ui/dropdown-menu.tsx</sub> | — | — | 3 |  |
| **DropdownMenuPortal**<br><sub>ui/dropdown-menu.tsx</sub> | — | — | 0 |  |
| **DropdownMenuRadioGroup**<br><sub>ui/dropdown-menu.tsx</sub> | — | — | 0 |  |
| **DropdownMenuRadioItem**<br><sub>ui/dropdown-menu.tsx</sub> | `value` | — | 0 |  |
| **DropdownMenuSeparator**<br><sub>ui/dropdown-menu.tsx</sub> | — | — | 6 |  |
| **DropdownMenuShortcut**<br><sub>ui/dropdown-menu.tsx</sub> | — | — | 0 |  |
| **DropdownMenuSub**<br><sub>ui/dropdown-menu.tsx</sub> | — | — | 0 |  |
| **DropdownMenuSubContent**<br><sub>ui/dropdown-menu.tsx</sub> | — | — | 0 |  |
| **DropdownMenuSubTrigger**<br><sub>ui/dropdown-menu.tsx</sub> | — | — | 0 |  |
| **DropdownMenuTrigger**<br><sub>ui/dropdown-menu.tsx</sub> | — | — | 13 |  |
| **Flex**<br><sub>ui/flex.tsx</sub> | — | — | 1 |  |
| **Form**<br><sub>ui/form.tsx</sub> | `clearErrors` `control` `formState` `getFieldState` `getValues` `handleSubmit` `register` `reset` `resetDefaultValues` `resetField` `setError` `setFocus` `setValue` `setValues` `subscribe` `trigger` `unregister` `watch` | — | 8 |  |
| **FormControl**<br><sub>ui/form.tsx</sub> | — | — | 8 |  |
| **FormDescription**<br><sub>ui/form.tsx</sub> | — | — | 2 |  |
| **FormField**<br><sub>ui/form.tsx</sub> | `name` `render` | — | 11 |  |
| **FormItem**<br><sub>ui/form.tsx</sub> | — | — | 10 |  |
| **FormLabel**<br><sub>ui/form.tsx</sub> | — | — | 8 |  |
| **FormMessage**<br><sub>ui/form.tsx</sub> | — | — | 9 |  |
| **GlowBorder**<br><sub>ui/aceternity/glow-border.tsx</sub> | — | — | 6 | Aceternity-style moving border: a slowly rotating conic gradient clipped to a 1-2px frame around the content. |
| **Input**<br><sub>ui/input.tsx</sub> | — | — | 38 |  |
| **Label**<br><sub>ui/label.tsx</sub> | — | — | 21 |  |
| **MovingBorder**<br><sub>ui/aceternity/moving-border.tsx</sub> | — | — | 4 | Aceternity "Moving Border", CSS-only: their version animates a glow dot along the border path with motion's getPointAtLength; the same read — light traveling around the edge — comes from rotating an oversized conic gradient behind a 1px-inset inner card. |
| **Popover**<br><sub>ui/popover.tsx</sub> | — | — | 4 |  |
| **PopoverAnchor**<br><sub>ui/popover.tsx</sub> | — | — | 2 |  |
| **PopoverContent**<br><sub>ui/popover.tsx</sub> | — | — | 4 |  |
| **PopoverTrigger**<br><sub>ui/popover.tsx</sub> | — | — | 2 |  |
| **ScrollArea**<br><sub>ui/scroll-area.tsx</sub> | — | — | 2 |  |
| **ScrollBar**<br><sub>ui/scroll-area.tsx</sub> | — | — | 0 |  |
| **Select**<br><sub>ui/select.tsx</sub> | — | — | 19 |  |
| **SelectContent**<br><sub>ui/select.tsx</sub> | — | — | 18 |  |
| **SelectGroup**<br><sub>ui/select.tsx</sub> | — | — | 3 |  |
| **SelectItem**<br><sub>ui/select.tsx</sub> | `value` | — | 18 |  |
| **SelectLabel**<br><sub>ui/select.tsx</sub> | — | — | 1 |  |
| **SelectScrollDownButton**<br><sub>ui/select.tsx</sub> | — | — | 0 |  |
| **SelectScrollUpButton**<br><sub>ui/select.tsx</sub> | — | — | 0 |  |
| **SelectSeparator**<br><sub>ui/select.tsx</sub> | — | — | 2 |  |
| **SelectTrigger**<br><sub>ui/select.tsx</sub> | (`aria-label`) **or** (`aria-labelledby`) | — | 18 |  |
| **SelectValue**<br><sub>ui/select.tsx</sub> | — | — | 18 |  |
| **Separator**<br><sub>ui/separator.tsx</sub> | — | — | 4 |  |
| **Sheet**<br><sub>ui/sheet.tsx</sub> | — | side: top/bottom/left/right | 1 |  |
| **SheetClose**<br><sub>ui/sheet.tsx</sub> | — | — | 0 |  |
| **SheetContent**<br><sub>ui/sheet.tsx</sub> | — | — | 1 |  |
| **SheetDescription**<br><sub>ui/sheet.tsx</sub> | — | — | 0 |  |
| **SheetFooter**<br><sub>ui/sheet.tsx</sub> | — | — | 0 |  |
| **SheetHeader**<br><sub>ui/sheet.tsx</sub> | — | — | 1 |  |
| **SheetOverlay**<br><sub>ui/sheet.tsx</sub> | — | — | 0 |  |
| **SheetPortal**<br><sub>ui/sheet.tsx</sub> | — | — | 0 |  |
| **SheetTitle**<br><sub>ui/sheet.tsx</sub> | — | — | 1 |  |
| **SheetTrigger**<br><sub>ui/sheet.tsx</sub> | — | — | 1 |  |
| **Skeleton**<br><sub>ui/skeleton.tsx</sub> | — | — | 12 |  |
| **Slider**<br><sub>ui/slider.tsx</sub> | (`aria-label`) **or** (`aria-labelledby`) | — | 6 |  |
| **Spinner**<br><sub>ui/spinner.tsx</sub> | — | — | 11 |  |
| **Spotlight**<br><sub>ui/aceternity/spotlight.tsx</sub> | — | — | 2 | Aceternity-style spotlight: a soft diagonal light sweep for sparse surfaces (404s, empty heroes). |
| **SpotlightCard**<br><sub>ui/aceternity/spotlight-card.tsx</sub> | — | — | 31 | Aceternity-style spotlight card: a radial glow that follows the cursor, plus a border highlight near the pointer. |
| **Switch**<br><sub>ui/switch.tsx</sub> | — | — | 17 |  |
| **Table**<br><sub>ui/table.tsx</sub> | — | — | 12 |  |
| **TableBody**<br><sub>ui/table.tsx</sub> | — | — | 11 |  |
| **TableCaption**<br><sub>ui/table.tsx</sub> | — | — | 0 |  |
| **TableCell**<br><sub>ui/table.tsx</sub> | — | — | 12 |  |
| **TableFooter**<br><sub>ui/table.tsx</sub> | — | — | 0 |  |
| **TableHead**<br><sub>ui/table.tsx</sub> | — | — | 12 |  |
| **TableHeader**<br><sub>ui/table.tsx</sub> | — | — | 11 |  |
| **TableRow**<br><sub>ui/table.tsx</sub> | — | — | 12 |  |
| **Tabs**<br><sub>ui/tabs.tsx</sub> | — | — | 10 |  |
| **TabsContent**<br><sub>ui/tabs.tsx</sub> | `value` | — | 10 |  |
| **TabsList**<br><sub>ui/tabs.tsx</sub> | — | — | 10 |  |
| **TabsTrigger**<br><sub>ui/tabs.tsx</sub> | `value` | — | 10 |  |
| **Textarea**<br><sub>ui/textarea.tsx</sub> | — | — | 9 |  |
| **ToggleGroup**<br><sub>ui/toggle-group.tsx</sub> | `type` | — | 7 |  |
| **ToggleGroupItem**<br><sub>ui/toggle-group.tsx</sub> | `value` | — | 7 |  |
| **Tooltip**<br><sub>ui/tooltip.tsx</sub> | — | — | 35 |  |
| **TooltipContent**<br><sub>ui/tooltip.tsx</sub> | — | — | 35 |  |
| **TooltipProvider**<br><sub>ui/tooltip.tsx</sub> | — | — | 35 |  |
| **TooltipTrigger**<br><sub>ui/tooltip.tsx</sub> | — | — | 35 |  |

## Shared components — `src/components/`

App-level patterns assembled from the primitives and reused across features.

| Component | Requires | Variants | Used | What it is |
|---|---|---|---|---|
| **CardGridSkeleton**<br><sub>LoadingSkeleton.tsx</sub> | — | — | 7 | Card-grid placeholder for entity-card pages (stations, voices, operators). |
| **CommandPalette**<br><sub>CommandPalette.tsx</sub> | — | — | 1 |  |
| **ComposerField**<br><sub>ComposerField.tsx</sub> | `label` | — | 5 | One labelled field in a {@link RuleComposer} row: a small muted label above a control. |
| **ConfigRow**<br><sub>ConfigRow.tsx</sub> | `label` | — | 1 |  |
| **ConfirmDialog**<br><sub>ConfirmDialog.tsx</sub> | `description` `onConfirm` `onOpenChange` `open` `title` | — | 17 |  |
| **DataBoundary**<br><sub>DataBoundary.tsx</sub> | `empty` `errorFallback` `loading` `query` | — | 12 |  |
| **DataTableColumnHeader**<br><sub>DataTableColumnHeader.tsx</sub> | `label` `onSort` `sort` `sortKey` | — | 8 | A clickable `<TableHead>` with a sort-direction indicator. |
| **EmptyState**<br><sub>EmptyState.tsx</sub> | — | — | 14 | The standard in-content empty state: dashed border, centred muted text. |
| **EntityActionsMenu**<br><sub>EntityActionsMenu.tsx</sub> | `onDuplicate` `onEdit` `onView` | — | 9 | Shared kebab menu for gallery cards and table rows. |
| **EntityDiffView**<br><sub>EntityDiffView.tsx</sub> | `entries` | — | 8 | Renders one value's diff: a field row shows before → after (git-style strike/highlight), a whole array item added/removed collapses to one tinted `+`/`−` line instead of exploding into its own fields. |
| **ErrorAlert**<br><sub>ErrorAlert.tsx</sub> | `error` | — | 4 |  |
| **ExportButton**<br><sub>TransferButtons.tsx</sub> | `onExport` | — | 2 |  |
| **FieldLabel**<br><sub>FieldLabel.tsx</sub> | `hint` `hintLabel` | — | 4 | A studio-form field label with its explanation behind a tooltip. |
| **FormInputField**<br><sub>FormInputField.tsx</sub> | `control` `label` `name` | — | 2 |  |
| **InfoHint**<br><sub>InfoHint.tsx</sub> | `label` | — | 28 | The one "explain this" affordance for the Bulletin-rules hub. |
| **ListSkeleton**<br><sub>LoadingSkeleton.tsx</sub> | — | — | 5 | Bordered row-list placeholder for the Bulletin-rules hub inventories and the broadcast log. |
| **LiveAssignmentNotice**<br><sub>LiveAssignmentNotice.tsx</sub> | `stations` | — | 4 | The one "this is live, saving affects air" banner for every versioned library studio (voices, delivery styles, bookends, pronunciation dictionaries). |
| **LiveEditConfirmationDialog**<br><sub>LiveEditConfirmationDialog.tsx</sub> | `changeNote` `entityName` `guidance` `isSaving` `nextVersion` `noun` `onConfirm` `onOpenChange` `open` `stations` | — | 4 | Shown before saving a NEW version of a library entity that is assigned to stations. |
| **LoadOlderButton**<br><sub>LoadOlderButton.tsx</sub> | `loading` `onClick` | — | 2 | Shared footer control for "Load older" cursor pagination — Activity and History both page this way. |
| **MetricCard**<br><sub>MetricCard.tsx</sub> | `label` | — | 3 |  |
| **NavigationBar**<br><sub>NavigationBar.tsx</sub> | — | — | 1 | Mobile top bar. |
| **Placeholder**<br><sub>Placeholder.tsx</sub> | `icon` `message` | — | 14 | The standard empty / loading / error state. |
| **RuleComposer**<br><sub>RuleComposer.tsx</sub> | `submit` | — | 5 | Shared authoring shell for the Bulletin-rules hub composers. |
| **RuleRow**<br><sub>RuleRow.tsx</sub> | `enabled` `label` `onToggle` | — | 8 | Shared list-row component for the Bulletin-rules hub and editorial guidance inventory. |
| **RuleTab**<br><sub>RuleTab.tsx</sub> | — | — | 7 | Shared scaffold for the Bulletin-rules hub tabs. |
| **RuleToolbar**<br><sub>RuleToolbar.tsx</sub> | — | — | 6 | The single row of chrome above every Bulletin-rules list: find, narrow, create — in that order, in the same place, on every tab. |
| **ScopeGroupedList**<br><sub>ScopeGroupedList.tsx</sub> | `empty` `groups` `renderRow` `rowKey` | — | 6 | Shared scope-grouped list for the Bulletin-rules hub: one bordered container with sticky per-scope headers ("{label} (n)") and dense, evenly-divided rows. |
| **SectionHeader**<br><sub>SectionHeader.tsx</sub> | `label` (`hint` `hintLabel`) | — | 0 |  |
| **StickyFilterRail**<br><sub>StickyFilterRail.tsx</sub> | — | — | 3 | Keeps list controls reachable while desktop operators scan long records. |
| **ViewToggle**<br><sub>ViewToggle.tsx</sub> | `onChange` `value` | — | 6 | Cards \| Table segmented control for entity-list pages. |

## Exported but never rendered

`AlertDialogOverlay`, `AlertDialogPortal`, `AlertDialogTrigger`, `CardAction`, `CardFooter`, `Command`, `CommandSeparator`, `DialogTrigger`, `DropdownMenuCheckboxItem`, `DropdownMenuPortal`, `DropdownMenuRadioGroup`, `DropdownMenuRadioItem`, `DropdownMenuShortcut`, `DropdownMenuSub`, `DropdownMenuSubContent`, `DropdownMenuSubTrigger`, `ScrollBar`, `SelectScrollDownButton`, `SelectScrollUpButton`, `SheetClose`, `SheetDescription`, `SheetFooter`, `SheetOverlay`, `SheetPortal`, `TableCaption`, `TableFooter`, `SectionHeader`

Each is either a sub-part consumed through a sibling export, or dead. `npm run knip` decides which.
