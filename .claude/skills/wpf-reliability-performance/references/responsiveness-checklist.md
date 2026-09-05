# Responsiveness checklist

## Dispatcher and async work

- The dispatcher performs only short state application and visual work.
- No `.Wait()`, `.Result`, blocking sleep, synchronous device/network/file call, or broad discovery runs on the UI thread.
- Cancellation reaches the actual operation and is not only a UI flag.
- Progress/status updates are throttled or coalesced when producers are faster than a human-visible refresh rate.
- An old async result cannot overwrite newer selection, connection, or navigation state; use generation/identity checks where operations can overlap.

## Live data and charts

- Sampling, persistence, calculation, and rendering frequencies are independently controlled.
- Dense series use chronological min/max envelope reduction and retain first/last points.
- A redraw does not allocate unbounded visual elements, brushes, handlers, or collections.
- Zoomed live charts retain their selected viewport while new data arrives.
- Expensive transformations are not repeated from binding property getters.

## Collections and XAML

- Large lists and grids keep WPF virtualization enabled unless a measured reason requires otherwise.
- Collection updates are batched where possible and never interrupt an active edit transaction.
- Bindings do not trigger repeated I/O, parsing, full-list scans, or object creation.
- Timers and event subscriptions have one identifiable owner and are removed during final cleanup.
- Hidden/reopened windows do not accumulate handlers or polling loops.

## Startup and navigation

- Render a usable shell before optional network discovery or integration reconnect.
- Restore only known endpoints at startup; broad discovery is an explicit action or bounded background task.
- Navigation does not reconstruct long-running device state unnecessarily.
- Slow optional services do not prevent login, dashboard display, window close, or application shutdown.

## Evidence

Record the scenario, dataset size/device count, before/after elapsed time or UI trace, and whether the dispatcher remained interactive. Treat fewer allocations or cleaner code as a hypothesis until the user-visible path is measured.
