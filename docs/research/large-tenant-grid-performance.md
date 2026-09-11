# Why the grids are virtual-mode ListViews

Reported symptom: the tool freezes on large tenants / a large user base.

## What was actually slow

A classic (non-virtual) `ListView` keeps one native item per row. Both grids are rebuilt from the
model caches on **every filter keystroke**, every column-header sort and the Teams/Users toggle,
and checkboxes made it worse: restoring N remembered checks is N separate native item-state
calls, and `CheckedItems.Count` - which the status line read twice per update - walks every item.

Measured on this machine (net48, Details view, `CheckBoxes = true`, `BeginUpdate`/`EndUpdate`
around the rebuild), with a standalone harness reproducing the old populate path:

| operation | 8,000 rows | 30,000 rows |
|---|---|---|
| build the `ListViewItem`s (managed) | 5 ms | 16 ms |
| `Items.AddRange` | 1,640 ms | 6,447 ms |
| re-check every row (`item.Checked = true` x N) | 1,439 ms | 5,802 ms |
| `CheckedItems.Count` x2 (one status update) | 122 ms | 287 ms |
| **one filter keystroke, everything checked** | **3,986 ms** | **13,118 ms** |
| one checkbox click (toggle + status update) | 79 ms | 281 ms |

So on a 30,000-user tenant every keystroke in the filter box cost ~13 s and every checkbox click
~0.3 s. The managed work is a rounding error; it is all native item traffic, and it scales with
the number of rows loaded, not the number on screen.

The "Select All" button (`0e67e1a`) made this reachable in one click: it checked all N rows, and
from then on every repopulation had N checks to restore.

## The fix

`VirtualMode = true` on both grids, with `VirtualGrid` (plugin project) as the binding:

- the control holds no rows - it asks for the ~30 it is painting via `RetrieveVirtualItem`;
- checked state lives in `SelectionMemory<object>` (Core) keyed by model, which is also what
  Add/Remove/Inspect read, so there is one source of truth instead of two that need syncing;
- a repopulation is a list swap plus `VirtualListSize`; a check is a `HashSet` write plus a
  `RedrawItems` on the affected rows.

Same harness, same 30,000 rows: populate **1 ms**, check-all **1 ms**, filter keystroke **2 ms**.

End-to-end against the real plugin control (rows injected into the caches, real mouse input, all
checks in `scratchpad/uitest`): 30,000 users / 10,000 teams / 2,000 roles - populate 31 ms, mode
switch 19 ms, Select All 17 ms, filter keystroke 19 ms, sort 38 ms. At 150,000 users / 50,000
teams nothing exceeds 160 ms (the sort).

## Virtual-mode facts worth knowing before editing `VirtualGrid`

Verified empirically (probe in the scratchpad), not from memory:

- `CheckedItems` and `CheckedIndices` **throw** `InvalidOperationException` in virtual mode, and
  so does enumerating `Items` (`foreach (ListViewItem i in lv.Items)`). The `Items[i]` indexer
  works, as do `EnsureVisible(index)`, `SelectedIndices`, `HitTest`, and `ListViewItem.Index` on
  an item that `HitTest` returned.
- `ItemCheck` / `ItemChecked` **never fire**, and clicking the checkbox glyph changes nothing by
  itself: the displayed check comes from whatever `RetrieveVirtualItem` last reported. That is
  why every click location toggles in `VirtualGrid.OnMouseClick` (the classic control's
  double-toggle compensation is gone with it), and why Space is handled explicitly.
- `RedrawItems(from, to, invalidateOnly: false)` is what re-fetches rows so the glyphs follow the
  memory; `Invalidate()` alone is enough after a `VirtualListSize` change.
- Set `VirtualListSize = 0` *before* swapping the backing list, or a repaint queued in between
  can ask for an index the new, shorter list doesn't have. `RetrieveVirtualItem` also answers
  out-of-range asks with a blank row rather than throwing inside a paint.

Testing note: the native control raises `Click`/`MouseClick` off its own `NM_CLICK` notification,
which only follows input that came through the real input queue. Posted or sent `WM_LBUTTONDOWN`
/`WM_LBUTTONUP` pairs reach `MouseDown`/`MouseUp` but produce no click, so the UI harness drives
clicks with `mouse_event`. (Sending rather than posting also deadlocks the control's drag-detect
loop for ~20 s per click - an artifact of the harness, not of the tool.)

## Loads

Unrelated to the grids but part of the same symptom: each load pages 5,000 rows at a time, so a
six-figure tenant is tens of round trips per entity with nothing on screen but a fixed
"Loading...". `RetrieveTeams`/`RetrieveUsers`/`RetrieveRoles` now take an optional `progress`
callback, reported once per page with the running count, which `LoadData` puts in the working
message.

Still unaddressed, if a tenant ever gets slow enough to warrant it: users are loaded on every
refresh even in Teams mode, and every `systemuser` row is retrieved (application and stub users
included - `RetrieveUsers` deliberately keeps disabled users, flagged rather than filtered).
