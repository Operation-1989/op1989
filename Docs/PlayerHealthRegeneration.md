# Player health regeneration

The player automatically recovers after damage through the existing `CombatHealth` component. By default, healing waits for **3 damage-free gameplay seconds**, then smoothly refills the health missing after the latest hit over **12 seconds**. Full recovery therefore takes **15 gameplay seconds after the last hit**.

Another valid hit immediately stops recovery and restarts that timeline from the newly reduced health. Nonpositive damage and hits ignored by cinematic protection do not interrupt it. Recovery follows scaled gameplay time, so a paused game does not heal the player. Firing the player's weapon alone does not interrupt recovery.

Only components with `isPlayer` enabled regenerate. Enemy soldiers retain their existing health behavior. Dead players cannot regenerate or revive through this system; the existing checkpoint respawn and manual R respawn still restore full health. `Restore()` also clears any pending regeneration.

The existing health bar displays gradual recovery. Its card heading changes to **RECOVERING** while health is refilling, then returns to **ASSAULT RIFLE** when full or interrupted.

## Inspector settings

On the player's `CombatHealth` component:

| Setting | Default | Purpose |
| --- | --- | --- |
| Regenerate Player Health | Enabled | Enables automatic recovery for player characters. |
| Regeneration Delay | 3 seconds | Required time without incoming damage. |
| Regeneration Duration | 12 seconds | Time to refill the health missing after the most recent hit. |

These defaults apply to existing player objects without changing scene assets. The respawn delay remains separate.

## Validation

Run **Operation 1989 → Combat → Validate Player Health Regeneration**. The validator uses the existing isolated combat scene and actual player/enemy health components, accelerates only its temporary test session, and restores the original editor scenes and clock settings afterward.

Checks cover the default timing, initial delay, partial recovery, ignored damage, full recovery, enemy exclusion, repeated-hit interruption, pause behavior, maximum-health clamping, disabling regeneration, death, and restore. Results are written to `Temp/PlayerHealth/validation.txt`.

All **13 Play Mode checks passed** during implementation. The runtime and editor scripts compiled successfully. No main-scene or shared enemy asset edits are needed for these defaults.
