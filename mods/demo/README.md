# Demo mods

Small mods that show what each kind can do. Install one by dropping it in Capri's `mods` folder, or from **Mainframe > CAPRI > Mods > Import mod**.

| File | Kind | What it shows |
|---|---|---|
| `checkers.capri-code` | Code mod | A brain tool, a WinForms window of the mod's own, a menu item, timers and the store. Ask her for a game of checkers, or open **Mods > Checkers** in her right-click menu. Click a piece, then where it goes. She plays the other side, with forced captures, multi-jumps and kings, and keeps the game between runs. Asks for `tools` only. Windows only, since the board is a WinForms window |
| `currency-rate.capri-tool` | Recipe tool | Takes a currency code, like `USD`, and gives her what one euro is worth in it today, from Frankfurter's European Central Bank rates |

## Recipe tools

A `.capri-tool` is a JSON recipe for a tool her brain can use: up to three `https` GETs to the sites in its `domains`, values kept from each JSON answer, and a line she's told back. It runs no code. An older Capri without recipe tools won't import them.

The currency tool needs no API key and sends only the currency code she looks up.
