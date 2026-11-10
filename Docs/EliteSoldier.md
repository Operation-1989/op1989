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
| `EliteServerObstacle` | Links an existing rack to its solid enclosing collider so the environment bootstrap does not generate separate colliders for every tiny mesh part. |

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

Cover candidates evaluated synchronously during one decision share a short-lived collision/exposure query batch. Exposure is also computed at most once per node within each search. The batch is discarded before movement; later decisions and movement checks query live geometry. The blocked-route regression checks that a later batch detects a newly introduced wall. This optimization changes query reuse, not the A* cost or heuristic.

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

Verified in the final room implementation: all **27 isolated Unity Play Mode checks** and **14 actual computer-room checks** passed, including a newly blocked route between query batches, embedded-muzzle safety, real A* server detours, wounded movement into cover, room containment, and return-to-post. The earlier 15 standalone utility/FOV/A* logic checks also passed. The complete runtime/editor source compilation passed. Reports are in `Temp/EliteSoldier/validation.txt` and `Temp/EliteSoldier/post-validation.txt`.

The guard is placed inside the **enclosed computer/server room**, rather than the building's central hallway. That room is identified by the `entry_3` architectural mesh containing the archive computer and its own `entry_3_door` doorway; the building-wide audio zone is not used as the physical room boundary. The post is near the computer, facing the doorway from inside. The graph's optional `navigationArea` BoxCollider constrains nodes, path endpoints, and movement segments to the room's XZ footprint. Existing server racks provide standing cover. PatrolSoldier and its shared assets are preserved.

**Operation 1989 → Elite → 5 Repair Entrance Guard Post** repositions an existing guard using those checks and backs up the scene to `Temp/EliteSoldier/before-post-repair.unity`. **6 Validate Main Scene Entrance Reaction** runs the actual army-base scene after runtime colliders are installed, temporarily places the existing player on the entrance approach, and checks FOV, detection, combat action, firing, and damage. It exits Play Mode and restores the editor scene; results go to `Temp/EliteSoldier/post-validation.txt`. This supplements the isolated tests, which cannot validate placement in the main map.

**7 Place Guard Inside Computer Room** backs up the scene to `Temp/EliteSoldier/before-computer-room.unity`, matches the room/building collision to the runtime environment setup, and rebuilds a dense graph with 0.6m spacing plus explicit rack-corner nodes. It selects an interior post with eye/muzzle visibility and a route to the room's own doorway approach, then authors protective reachable markers around the actual server racks. Rack collider volumes preserve physical cover while avoiding thousands of redundant generated part colliders. Fresh integration through command 4 also applies this room setup automatically.

The saved post is **(-7.41, 0.07, 24.34)**, with **148 nodes** and **9 cover candidates**. In the runtime entrance scenario, **4 cover positions** were protective and reachable, all requiring valid obstacle detours; the guard revalidates eligibility against its current observation. The 13 racks used 39 colliders in that test. Current placement counts are written to `Temp/EliteSoldier/computer-room.txt`; a rendered preview is written to `Temp/PlayerSoldier/elite-computer-room.png`. With a room boundary assigned, command 6 verifies server-cover paths, obstacle detours, actual wounded-guard movement into cover, and returning to the computer-side post after memory expires. Its temporary player health increase and damage are confined to Play Mode.

The earlier building-entrance reaction checks have been superseded by the enclosed-room checks above. The room test waits for scaled gameplay time and gameplay frames, with a wall-clock timeout, so startup stalls do not consume the utility commitment period. Door interaction during a normal mission, search behavior in different player escape directions, combat difficulty, and animation appearance still benefit from manual playtesting.

The automated checks cover FOV/range/occlusion, hidden movement and memory decay, utility priority and commitment, A* cost and failure cases, protective/reachable cover, obstacle detours, real firing and occlusion, pursuit limit, returning to post/facing, damage, death, and health restoration. Check `validation.txt` for actual PASS/FAIL outcomes; having the harness in the repository is not evidence that its Play Mode checks ran.

Manual checks are still needed for main-scene door movement, room collision clearances, animation/muzzle appearance, tuning combat difficulty, and scene reload/restart. The graph supports a small planar floor, not stairs, jumping, or multi-storey navigation. Cover is standing cover; the soldier can wait behind it but does not animate crouching, blind-fire, or a dedicated peek. Search tries the last observation and eight surrounding reachable points. Exposure is a physics-ray estimate of the current environment from a remembered threat position, not a prediction of the player's hidden movement.

## Commit guidance

The base feature was introduced in `fb5b0c3` on `feature/elite-soldier`. The computer-room placement and navigation refinements cover the groups below. Avoid `git add .` because the repository contains extensive unrelated changes.

Logical review groups:

1. `feat(elite): constrain navigation to room and batch cover queries` — `EliteNavigationGraph.cs`, `EliteGuardController.cs`, `EliteServerObstacle.cs` and metadata, `WorldInteractionBootstrap.cs`, and the updated isolated regression checks.
2. `fix(elite): place guard beside computer with reachable server cover` — the elite-related changes to `Assets/army base.unity`, `EliteSoldierSetup.cs`, `EliteGuardPostValidation.cs` and metadata, and this document. Review scene hunks carefully because this file also contains pre-existing work.

Do not commit anything under Temp, Library, or Logs. Before each commit inspect `git diff --cached --stat` and `git diff --cached`.
