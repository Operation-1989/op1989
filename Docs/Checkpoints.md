# Main building entrance checkpoint

The existing inside-gate respawn remains the default until the player enters the main building. Walking into the central interior corridor activates **Main building entrance**, displays a brief **CHECKPOINT REACHED** notification, and makes that the destination for later automatic or R-key respawns.

The new respawn marker is **(1.50, 1.07, 20.26)**, just inside the front entrance, facing into the building. Setup derives it from the actual `door_middle`/`arch_middle` geometry and interior floor, verifies player-sized clearance and ground support, and keeps the activation volume 0.6m inside the paired entrance planes. Its corridor volume supports entry through either normal main-building doorway; merely approaching from outside does not activate it.

Activation does not heal the player, reset enemies, or change the theft objective. Death still uses the existing mission/alarm reset and restores full player health. Convoy progress and enemy damage remain intact across respawns. Leaving the building or dying does not discard the earned checkpoint. A full intro retry, mission replay back to the gate, or a fresh Play session clears it for a new run. Progress is per-run, not saved across application restarts.

## Components and setup

- `PlayerCheckpoint` owns the interior activation volume and respawn marker. It accepts only its configured living player while first-person controls are active. Root-position polling supplements CharacterController trigger callbacks, so imported passage collision exclusions cannot prevent activation.
- `PlayerCheckpointState` stores the latest earned checkpoint on the player and presents the notification. Higher-priority checkpoints can replace it; equal/lower priorities do not repeatedly reactivate it.
- `CombatHealth.Respawn()` asks the intro system to restore the current checkpoint. The existing explicit `RespawnInsideGate()` API remains available and clears saved checkpoint progress. Without an intro system, health respawning can still use the player's saved checkpoint.
- The environment bootstrap preserves checkpoint trigger colliders even when their names contain “entrance.” The death panel names the active checkpoint instead of always promising a gate respawn.

Use **Operation 1989 → Combat → Install Main Building Entrance Checkpoint** in the army-base scene to create/update **Main Building Entrance Checkpoint**, its trigger, the respawn marker, and the player state component. Setup saves a pre-change scene copy to `Temp/BuildingCheckpoint/before-install.unity`. Select the checkpoint to see the activation box and spawn-facing gizmos.

## Validation

**Operation 1989 → Combat → Validate Main Building Checkpoint** exercises the actual main scene: gate fallback, invalid actor/control/death cases, walking across the interior boundary, activation without healing, persistent progress after exit/death, manual/timed/R respawns, camera and input restoration, enemy/convoy state preservation, and full-retry reset. Temporary test input and scene changes are restored after Play Mode. Results are written to `Temp/BuildingCheckpoint/validation.txt`.

All 24 checks passed in the main scene, including opening the existing entrance door and walking through its gap with the CharacterController.
