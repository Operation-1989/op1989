# Elite soldier: control room defense

The chamber is the existing control room containing the archive computer. This feature has its own soldier prefab, Animator Controller, and AI. PatrolSoldier and its shared assets are not edited. No packages are required.

## Components

| Component | Responsibility |
| --- | --- |
| `ElitePerception` | Range/FOV/eye rays, observation timestamp, last observed position and aim point, linear memory decay. Only this component samples the target position, and hidden samples never leave the visibility test. |
| `EliteUtility` | Eligible scores, switching margin, and minimum commitment. |
| `EliteGuardController` | Defensive priorities, reachable/protective cover selection, bounded search, rifle safety, damage/death/restore, debug overlay. |
| `EliteNavigationGraph` | Authored ground points, capsule-validated edges/endpoints, inspectable A*, path cost and expanded-node count. |
| `EliteMovement` | Sole locomotion writer: CharacterController, acceleration, rotation, path following, block/stall replanning, and actual-motion animation. |
| `EliteCoverPoint` | Authored standing position, verified protection from the last observed threat. |

The existing `CombatHealth` and `RifleWeapon` remain the damage and weapon interfaces. The elite detects damage by health changes; damage gives no attacker coordinates. It stops on death and detects restoration through `CombatHealth.Restore()`. The existing health component supplies its fall-over presentation; no death, reload, crouch, or cover clips are invented.

## Utility formulas

Inputs are clamped to [0,1]: health H, range suitability R, memory confidence C, distance from post D, and exposure E. R = 1 − clamp(abs(distance − preferredRange)/preferredRange); C = clamp(1 − observationAge/memorySeconds); D = clamp(distanceFromPost/defensiveRadius). Exposure is 1 when a ray from the last observed threat to the guard is unobstructed, otherwise 0.

| Action | Eligibility | Score |
| --- | --- | --- |
| Defend | Within 0.35m of post | defendWeight × (visible ? 0.35 : 1) |
| Engage | Visible threat inside defensive radius | engageWeight × (0.45 + 0.35R + 0.20H) |
| Cover | Protective reachable cover, usable memory, threat inside radius | coverWeight × (0.25 + 0.45(1−H) + 0.30E) × C |
| Search | Hidden threat with usable memory inside radius and within search timeout | searchWeight × C |
| Return | Away from post | returnWeight × (0.35 + 0.65D + ((!memory or !inside) ? 0.65 : 0)) |

An ineligible score is −1. A challenger must exceed the current score by `switchingMargin` after `minimumCommitment`. Invalid actions interrupt immediately; death stops all actions. Cover/search are temporarily suppressed after a failed route to prevent retry thrashing. Visible threats outside the defensive radius cannot lure the soldier away. Engage holds a defensive firing position rather than pursuing the player.

## A* reasoning

Edges cost distance in metres plus `max(0, exposurePenalty) × exposure(destination)`, where the penalty is also in metres. The heuristic is straight-line distance in metres. Nonnegative extra costs and the triangle inequality make it admissible and consistent. The implementation scans an open set instead of using a heap because the graph is small; parents reconstruct an ordered path including its exact endpoints.

Nodes have a maximum connection distance. Capsule overlaps and casts validate standing space, the complete edge, and the destination while ignoring the guard's colliders and triggers. Exact start/goal connectors receive the same checks; a nearby point across a wall cannot be selected by snapping. Invalid indices, nonfinite positions, same-position goals, disconnected graphs, and blocked edges have explicit outcomes. Movement checks the next segment and step, replans after goal changes, blockages, or stalls, and rate-limits failed queries. A failed behavior returns toward the post; if that route also fails, the guard stops safely and retries.

Threat exposure uses only the current visible observation or decaying memory. No hidden live target position is supplied to navigation, cover, aiming, or search.

## Setup and tuning

Use **Operation 1989 → Elite** in order:

1. **Inspect Control Room** writes the actual room bounds and computer location to `Temp/EliteSoldier/inspection.txt`.
2. **Build Isolated Test Scene** creates `Assets/Prefabs/Agents/EliteSoldier.prefab`, `Assets/Animations/EliteSoldier.controller`, and `Assets/Scenes/EliteSoldierTest.unity`. It copies the existing player so movement, scope, and rifle controls remain compatible. It preserves the currently open scene. The command refuses to overwrite an existing test scene.
3. **Validate In Play Mode** runs automatic checks and writes `Temp/EliteSoldier/validation.txt`. Unity's Play Mode start-scene override runs only the isolated scene and restores the open editor scenes, including unsaved changes, after testing. The previous override is then restored. A success marker is written only after all checks pass and Play Mode exits.
4. **Integrate Into Control Room** requires a successful test marker and the actual room, archive terminal, floor, and player. It first saves a backup of the current scene to `Temp/EliteSoldier/before-integration.unity`. It adds the separate defense root, ground graph, guard post, and candidate cover around existing room servers/desks/cabinets/columns. Missing solid colliders are added to relevant substantial mesh surfaces. No cover furniture is substituted for the main scene's existing objects.

Tune the guard instance's weights, defensive radius, preferred range, search timeout, aim tolerance, and shot interval. Tune perception's eye/target offsets, FOV, range, layers, and decay independently. The default target offset (+0.45m) matches this project's player root near the capsule center. Navigation radius must be at least the soldier CharacterController radius. Keep ground nodes on one walkable floor with enough clearance. Assign explicit cover markers at standing positions, not inside furniture.

Enable `debugOverlay` on the guard for action scores, eligibility, visibility, memory, and A* statistics. Select the guard for FOV, defensive boundary, last position, cover, and path gizmos. Enable the graph's `drawGraph` for nodes and validated connections; keep this off during normal play because it performs physics checks.

## Validation and practical limits

Verified in this implementation: all 26 automatic Unity Play Mode checks passed, including the newly blocked route and embedded-muzzle cases. All 15 additional standalone utility/FOV/A* logic checks passed. The complete runtime/editor source compilation passed with zero errors. Reports are in `Temp/EliteSoldier/validation.txt` and `Temp/EliteSoldier/integration.txt`.

The main scene now contains **Control Room Elite Defense**, a guard post at **(-9.55, 0.07, 20.11)**, **242** ground nodes, and **24** cover candidates around existing objects. The pre-integration editor scene, including its previous unsaved changes, was saved as `Temp/EliteSoldier/before-integration.unity`. PatrolSoldier and the shared patrol/player controllers were verified unchanged.

The automated checks cover FOV/range/occlusion, hidden movement and memory decay, utility priority and commitment, A* cost and failure cases, protective/reachable cover, obstacle detours, real firing and occlusion, pursuit limit, returning to post/facing, damage, death, and health restoration. Check `validation.txt` for actual PASS/FAIL outcomes; having the harness in the repository is not evidence that its Play Mode checks ran.

Manual checks are still needed for main-scene door movement, room collision clearances, animation/muzzle appearance, tuning combat difficulty, and scene reload/restart. The graph supports a small planar floor, not stairs, jumping, or multi-storey navigation. Cover is standing cover; the soldier can wait behind it but does not animate crouching, blind-fire, or a dedicated peek. Search tries the last observation and eight surrounding reachable points. Exposure is a physics-ray estimate of the current environment from a remembered threat position, not a prediction of the player's hidden movement.

## Suggested commits (user executes)

All changes are left unstaged on `feature/elite-soldier`. Review each group and commit it yourself. Avoid `git add .` because the repository already contains extensive unrelated changes.

1. `feat(elite): add perception memory and utility decisions` — Elite folder metadata, `ElitePerception.cs`, `EliteUtility.cs`, and their `.meta` files.
2. `feat(elite): add clearance-aware custom A-star navigation` — `EliteNavigationGraph.cs`, `EliteMovement.cs`, and metadata.
3. `feat(elite): add defensive guard cover and combat` — `EliteGuardController.cs`, `EliteCoverPoint.cs`, and metadata.
4. `feat(elite): add soldier assets test scene and validation` — elite prefab/controller/test scene and metadata; `EliteSoldierSetup.cs`, `EliteSoldierValidation.cs`, and metadata; this document. Review the earlier commits as component building blocks; the complete Unity feature is assembled by this commit.
5. `feat(elite): place guard in control room using existing cover` — only the elite-related changes to `Assets/army base.unity`. Use `git add -p -- "Assets/army base.unity"` and inspect every hunk because this file had pre-existing work. Include only any new metadata attributable to this feature.

Do not commit anything under Temp, Library, or Logs. Before each commit inspect `git diff --cached --stat` and `git diff --cached`. The setup/validation scripts refer to the runtime components, so source-only commits 1–3 are logical building blocks; compile the complete group before reviewing the finished feature.
