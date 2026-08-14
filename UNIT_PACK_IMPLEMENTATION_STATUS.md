# Unit pack implementation status

Source of truth: `MedievalChessUpdated.xlsx`, using each pack sheet over the older generic `Rules` sheet where they conflict. `ChessPack` and `LegacyPack` are intentionally excluded from the workbook update.

## Implemented on `unit-packs-circle`

- All non-Legacy workbook units and packs are registered, including Norse and Wild West.
- Chess definitions retain their pre-workbook values and movement semantics.
- `Circle` exists end-to-end in `Shape`, `RuleShape`, local square generation, shared movement/attack validation, pathfinding, minimum ranges and UI text.
- Workbook minimum movement ranges work (`Pegasus 2-4 Circle`, `Sleipnir 4-6 Circle`).
- Farm income is 10.
- Baron aura constants use the doubled values.
- Princess obstacle/terrain attack bypass is represented in shared LOS rules.
- Artemis can attack through forest in shared LOS rules.
- Raider gets +2 movement when moving forward.
- Sleipnir ignores ordinary terrain movement costs, rivers and blocking units while travelling; landing still uses the board's normal legality callback.
- Ninja supports three attacks per turn in local piece state.
- Samurai rejects projectile attacks through local attack validation.
- Shieldbearer survives its first lethal hit at 20 HP.
- Emperor transforms into Terracotta Warrior on its first lethal hit.
- Zombie transforms into Flesh on lethal damage; Flesh returns to Zombie on its next owner turn.
- Vampire heals 20 after attacking, capped at maximum health.
- Berserker uses 40 attack at 20 HP or less.
- Ghoul expires after four owner turns.
- Tumbleweed expires after three owner rounds.
- Existing Cavalier, Guard, Ox, Spy, Engineer, Mercenary, Elephant, Ballista, Palace and Emissary hooks remain available and use the updated definitions/shared helpers where their game-loop code is not hard-coded.
- Regression tests cover Circle geometry, min/max ranges, catalogue completeness, Chess movement preservation, state-driven abilities, Raider movement, Samurai projectile immunity and Artemis/Princess LOS.

## Still needs a central `Game1.cs` / authoritative-server hook

These are intentionally **not** marked implemented merely because constants/descriptions/helpers exist:

- Bombard: existing local splash is still hard-coded as 10; workbook requires 20.
- Engineer: existing local barricade HP is still hard-coded as 20; workbook requires 40.
- Mercenary: existing local payroll is still hard-coded as 10; workbook requires 20, including the 10-19 gold unpaid/fire case.
- President: pay 5 per turn or lose.
- Dragon: attack every unit in its forward attack line/range.
- Goblin Royalty: four separate royal goblins and lose only when all four die.
- Wizard: 3x3 attack damage centred on the target.
- Dragonborn: delayed 10-damage burn at the start of the attacker's next turn, including proper death/victory handling.
- Orc: attack all units in range. Its workbook purchase cost is blank, so it is currently excluded from purchasable units instead of being made free.
- Phantom: possess/unpossess a friendly unit and transfer Royal status.
- Zeus: 10-damage adjacency chain after the initial target.
- Chimera: +20 damage when attacked from behind.
- Artemis: +10 damage against targets in forest.
- Terrorist: full attack-range explosion on death and self-death after attacking.
- Tank: facing-only attack; off-axis attack input should rotate without firing.
- Sleipnir: full lake/otherwise-impassable-terrain traversal needs the board legality callback to distinguish board edges/barricades from terrain.
- Online/server parity for the new stateful abilities (especially Ninja attack counts and Samurai immunity) still needs the authoritative server protocol/handlers updated.

The remaining hooks are concentrated in private methods in `Game1.cs` (`ApplyTurnEconomy`, Engineer special handling, movement legality/cost callbacks, attack resolution/Bombard handling and the special-ability dispatcher) plus corresponding authoritative server handlers.
