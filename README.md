# Enter the Gungeon – Co-op Bot (SOS-Games fork)

A BepInEx mod that adds an AI-controlled second player to *Enter the Gungeon*. The bot follows you, fights, dodges, and does what you order with hotkeys. It uses the game's own co-op systems (second player, ghost/revive, co-op camera and UI).

> **Fork notice:** this is a fork of [ItsYuriil/Enter-The-Gungeon-Coop-Bot](https://github.com/ItsYuriil/Enter-The-Gungeon-Coop-Bot). All credit for the original mod and the bot AI goes to **ItsYuriil**. This fork (current version **1.5.1**) adds save/continue support, gear persistence, trading and more. The BepInEx log shows `CoopBot (SOS-Games fork) v1.5.1 loaded` so you can tell builds apart.

## Features

### Original mod
- **Summon / dismiss** a bot in any run; choose which character it plays (Cultist, Pilot, Convict, Marine, Hunter, Robot, Bullet, Paradox, Gunslinger).
- **Combat:** targets and shoots enemies with lead, slight human-like aim error, reloads smartly, handles charge weapons, drops empty weapons, can use blanks and active items.
- **Dodging:** predicts incoming projectiles and weaves or rolls (with varied reaction times); avoids pits, harmful goop/fire and telegraphed danger zones (e.g. sky rockets).
- **Cover:** flips tables and uses cover, with a configurable preference.
- **Orders (H):** open chests, interact with objects, pick up guns, smash breakables, regroup, or kill the enemy under the cursor.
- **Co-op rules:** works with doors and elevators, becomes a ghost when killed (blocks bullets until revived), camera follows the bot if you are down.
- **Shared loadout:** the bot's guns/items are stored when dismissed and restored when summoned again (same session).
- If you die outside a boss room, the run ends as in single player.
- **Pause-menu "Coop Bot Menu"** to rebind keys and set cover preference.

### Added in this fork
- **Save & continue fix:** after *Save & Quit* → *Continue*, the game rebuilds player 2 as a plain co-op player. The bot now automatically **re-attaches** to that player (no idle P2, no duplicate bot when you press H, camera follows you again). Pressing H while a P2 already exists takes it over instead of spawning a second bot.
- **Gear persistence:** when the game writes its mid-game save (leaving a floor), the mod snapshots the bot's guns + ammo, held gun, passives, actives, health, armor and blanks to `BepInEx\config\CoopBot.loadout.txt`, tied to that save. On continue the bot's inventory is reconciled to the snapshot (missing items added, wrong ones removed, nothing duplicated). Old snapshots from other runs/saves are ignored; the snapshot is cleared when the run ends.
- **Character + costume restore:** on continue, player 2 comes back as the bot's saved character (not always the Cultist), with the right starter guns and alternate costume; the game's own save restore then applies items and health to that body.
- **Death drops (`DropItemsOnDeath`, default on):** when the bot dies and becomes a ghost, its droppable non-starter guns and items are dropped where it fell, spread out so they don't stack. With the option off, the bot keeps its gear and gets it back when revived (also across save/continue).
- **Auto heal/armor pickup (`AutoPickupHealing`, default on):** an injured bot walks over to hearts/armor in its room. It shares sensibly: it leaves hearts for you when you are more hurt (by fraction of max health) and only takes armor when it has less than you. In combat it only grabs a nearby heart when low (< 35% health).
- **Trade window (L):** outside combat, opens a window with your gear and the bot's gear side by side; **Give >** / **< Take** buttons move individual guns, active and passive items (using the game's drop → pick-up path, so ammo/charges are kept). Starter and undroppable items are greyed out. Your movement is blocked while the window is open; it closes on L, the Close button, pausing, combat, level load or the bot dying.
- **Gear HUD (K):** a small overlay listing the bot's health/armor/blanks, guns (with ammo, held gun marked `>`), active items (with cooldown) and passive items.

## Requirements

- *Enter the Gungeon* (Steam, Windows)
- [BepInEx 5](https://thunderstore.io/c/enter-the-gungeon/p/BepInEx/BepInExPack_EtG/) – easiest via **r2modman** / Thunderstore Mod Manager (BepInExPack EtG)
- [Mod the Gungeon API](https://thunderstore.io/c/enter-the-gungeon/p/MtG_API/Mod_the_Gungeon_API/) (recommended; its F2 console is handy for testing)
- No other mod dependencies (the plugin only needs BepInEx + Harmony, which ship with BepInEx)
- To build from source: **.NET SDK 8 or newer** (the project targets `net35` through the `Microsoft.NETFramework.ReferenceAssemblies` package; no separate targeting pack needed)

## Install

1. Install BepInEx (and Mod the Gungeon API) – with r2modman, or manually into the game folder – and run the game once.
2. Copy `CoopBot.dll` to `<BepInEx folder>\plugins\CoopBot\`.
   - Manual install: `<game folder>\BepInEx\plugins\CoopBot\`
   - r2modman: `%AppData%\r2modmanPlus-local\ETG\profiles\<Profile>\BepInEx\plugins\CoopBot\` (launch the game through r2modman with that profile)
3. Start a run and press **H**.

## Build from source

The project needs two paths:

- `GamePath` – the game folder (DLLs are read from `<GamePath>\EtG_Data\Managed`)
- `BepInExPath` – the BepInEx folder (`BepInEx.dll` and `0Harmony.dll` from `<BepInExPath>\core`; the built DLL is copied to `<BepInExPath>\plugins\CoopBot\`). Defaults to `<GamePath>\BepInEx`.

**Option 1 – local props file (recommended):** copy `CoopBot.local.props.example` to `CoopBot.local.props` (git-ignored) and set your paths:

```xml
<Project>
  <PropertyGroup>
    <GamePath>C:\Program Files (x86)\Steam\steamapps\common\Enter the Gungeon</GamePath>
    <!-- r2modman keeps BepInEx in its profile, NOT in the game folder: -->
    <BepInExPath>$(APPDATA)\r2modmanPlus-local\ETG\profiles\Default\BepInEx</BepInExPath>
  </PropertyGroup>
</Project>
```

**Option 2 – command line:**

```
dotnet build -c Release -p:GamePath="C:\path\to\Enter the Gungeon" -p:BepInExPath="C:\path\to\BepInEx"
```

Then build with `dotnet build -c Release`. The output is `bin\Release\net35\CoopBot.dll`, and it is copied into `<BepInExPath>\plugins\CoopBot\` automatically when that folder exists. **Close the game before building** (the DLL is locked while it runs).

## Controls

All keys are rebindable in the BepInEx config; H, J, N, U and B also in the pause-menu *Coop Bot Menu*.

| Key | Action |
|-----|--------|
| H | Summon the bot (or take over an existing player 2); afterwards order it: interact / pick up / smash / attack the enemy under the cursor, or regroup |
| Shift+H | Dismiss the bot (keeps its loadout for the next summon) |
| J | Cycle the bot character (only while no bot is active) |
| N | Bot drops its equipped weapon (never a starter) |
| U | Bot uses its active item |
| B | Bot drops its active item |
| K | Toggle the bot gear HUD |
| L | Open/close the trade window (outside combat) |

The game's own default keys (WASD, E, R, Q, F, G, I, Tab, Space, Esc, …) are not used by the mod.

## Configuration

File: `BepInEx\config\com.coopbot.etg.cfg` (created on first launch).

| Section | Setting | Default | Meaning |
|---|---|---|---|
| Controls | OrderKey, CycleCharacterKey, DropWeaponKey, UseItemKey, DropItemKey | H, J, N, U, B | Keys above |
| UI | GearHudKey / TradeWindowKey | K / L | Gear HUD and trade window keys |
| UI | ShowHints | true | On-screen messages from the bot |
| Bot | Character | Cultist | Character the bot plays as |
| Bot | CoverPreference | 0.5 | 0 = never hides, 1 = hides a lot |
| Bot | AutoPickupHealing | true | Bot collects hearts/armor when it needs them more than you |
| Bot | DropItemsOnDeath | true | Bot drops its non-starter gear when it dies (false = keeps it until revived) |
| State | BotActive | – | Internal; lets the bot re-attach after continuing a saved run |

Other files: `BepInEx\config\CoopBot.loadout.txt` – the bot's gear/character snapshot for the current save (managed automatically; safe to delete).

## Known limitations

- Gear gained mid-floor is captured at the next floor transition; continuing a save rewinds to the start of the floor, as the game does.
- Items re-created from the snapshot (on continue, or on revive with `DropItemsOnDeath = false`) start with fresh internal state (charges/synergy state); items moved via the trade window keep theirs.
- The trade window does not pause the game, so it is only available outside combat.
- Older saves without a character in the snapshot restore the bot as the Cultist once; later floors restore correctly.

## Notes

- Not affiliated with Dodge Roll or Devolver Digital. No game files are included in this repository.
- Behaviour is tuned by playtesting; expect rough edges. Bug reports with `BepInEx\LogOutput.log` are welcome.
