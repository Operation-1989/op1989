# Entrance truck intro

Open `Assets/army base.unity` and enter Play Mode. The player starts behind the right-hand sandbags. After the aerial shot, walk around their south end and follow the truck through the white arch. If the entry window expires before you enter, press **R** to retry the intro.

## Scene setup

- `Intro Truck Cutscene` owns `IntroTruckSequence`, the dedicated M923 instance, route markers, gate hinges, and local entrance footing/threshold.
- `Intro Player` is copied from the existing `AgentTesting` player, including its first-person camera and controller. The source scene and player code are unchanged.
- The copied player's capsule renderer is disabled. Its textured soldier visual and player-specific animation setup are documented in `Assets/PlayerSoldier/README.md`; its original controller and collision setup remain in place.
- The existing root `Main Camera` is the aerial camera. `Intro Player/Main Camera` is the first-person camera. The sequence switches their Camera and AudioListener components together.
- The imported gate has no existing mechanism. Its grille was separated into two hinged leaves, using the original material. The white arch stays stationary. Original FBX assets are unchanged; the scene instance uses the generated frame/leaf meshes in this folder.
- The entrance curb has a local opening and threshold. A small ground extension supports walking around the outside sandbags; the original base ground ends at Z=-100.
- The truck has a kinematic body and collider, with no patrol/chase AI. Its route passes through the arch, turns right at the inside junction, and parks on the paved side road near `(23, 0.2, -77.8)`.

## Inspector settings

Select `Intro Truck Cutscene`:

| Setting | Current value |
| --- | --- |
| Truck speed | 3 m/s |
| Acceleration | 1.5 m/s² |
| Turn speed | 45 degrees/s |
| Aerial duration | 3.5 seconds |
| Aerial position | (20, 18, -113) |
| Aerial drift | (-3, 0, 3) |
| Gate opening delay | 1 second |
| Player entry window after handoff | 20 seconds |
| Gate wait point | Route 1 |

Move the children of `Truck Route` to change the route; `Parking` is the last point. Keep Route 1 outside the gate, allowing room for the truck's front overhang. `Ground Surfaces` lists driveable floors that the truck samples under its axles; other colliders remain obstacles.

Select `White Gate Controller` to adjust the two opening angles, 2.5-second travel time, and world-space safety volume. The safety volume covers both leaves' sweep. The gate waits for the truck to clear and holds/reopens while the player, truck, CharacterController, or rigid body occupies that volume.

The intro resets on scene replay, including the editor profile with domain and scene reload disabled. Retrying resets only intro-owned state and the player's starting position; patrol state is untouched.

## Validation tools

`Operation 1989 > Intro > Validate Sequence` runs from the saved scene in Edit Mode. It exercises the initial camera/input lock, handoff, missed-entry retry, actual keyboard-driven entry, gate occupancy interlocks, parking, and scene replay, then returns to Edit Mode. It temporarily adds a virtual keyboard for the walking check and removes it afterward. Results and screenshots are written to `Temp/IntroTruck`.

`Report Runtime` and `Capture Active Camera` provide additional diagnostics. `Build Scene` is a one-time setup utility and refuses to create a duplicate intro.

The completed run passed all 23 intro checks. Separate Play Mode smoke checks confirmed movement in `AgentTesting` and movement with the original animator/controller enabled on the actual `PatrolSoldier` prefab in `SoldierAnimationTesting`. The patrol prefab and existing patrol scripts remained byte-for-byte unchanged. Reports are preserved in `CleanupAudit/IntroTruck`.

Human review remains useful for mouse-look feel, shot pacing, and visual wheel contact during turns. Automated checks verify control/state transitions and passage/parking, rather than subjective presentation.
