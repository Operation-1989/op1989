# Drone-guided jeep interception

The active `TacticalDroneController` patrols its aerial route, visually acquires the player after base entry, and follows using live obstacle-cleared aerial paths. It has no weapon. `DronePlayerSurveillance` uses the tactical perception result directly and reports current, genuinely visible positions every 0.2 seconds through `DroneAlertSystem`. Cover, concealment, loss of sight, destruction and disabled sensors clear contact. A 1.25-second report timeout also prevents stale locks.

`JeepPursuitBootstrap` creates the alert bus and knowledge base when needed, connects the active tactical drone, and installs one `JeepAStarPursuit` on each road-patrol jeep. The disabled legacy survey drone stays disabled. Legacy competing motion/spotting components are disabled.

## Jeep behavior

- **Patrol:** A* connects the existing authored waypoints. A building across a straight waypoint leg produces a detour. Invalid waypoints use nearby reachable approach cells; no route produces a retry or another waypoint rather than driving through the obstruction.
- **Intercept:** On a drone report or the jeep's own visual contact, compute an interception point between the observed player position and the main entrance, with a bounded lead based on observed approach velocity. Use uniform distance costs for the shortest safe route on the clearance grid; replan for meaningful target movement at most every 0.35 seconds. No hidden player position is used for targeting.
- **Block:** Park broadside across the approach near the interception point. Full-body collision checks prevent ramming the player or obstacles.
- **Search:** On loss of contact, approach the last known position, wait eight seconds, then find a reachable patrol waypoint and continue the loop. An unreachable lost target cannot leave the jeep retrying forever.

The same driver owns all four phases. `BaseRoadJeepPatrol` retains waypoint data and public patrol metrics; it does not simultaneously move the jeep. Engine audio follows the active driver during patrol as well as pursuit.

The jeep scans every 0.15 seconds with a 48 m range and 130 degree forward field of view; within 8 m it notices the player on either side. Once alerted, it watches around the recent contact while intercepting/searching, so parking broadside does not drop a visible target. Sight rays aim inside the actual player body bounds, sampling the torso and shoulders rather than assuming a foot-level player pivot. Solid cover blocks detection. Its own sightings continue after the drone is destroyed. Losing both sources starts the last-known-position search.

`JeepDamage` accepts player rifle hits: 150 health means six default 25-damage hits stall the engine. Damage produces thick smoke, with intermittent engine sparks when disabled. A disabled jeep remains stationary for 30 gameplay seconds, ignores further damage during that interval, then fully repairs and resumes interception if it has contact or returns to patrol. Partial damage repairs after 30 damage-free seconds. The jeep remains a solid obstacle while disabled; it stops moving to intercept the player. The mission HUD shows remaining armour only while partially damaged. Breakdown is communicated by smoke and sparks; no repair countdown, repair messages or AI intentions are displayed.

## Navigation guarantees and limits

A* minimizes distance during interception on the sampled grid, with no diagonal corner cutting and conservative full-vehicle turning clearance. Patrol/return retain road preference. Smoothing must preserve cell clearance and cost. Ground sampling prioritizes known roads/terrain and avoids choosing a low building roof as the road. Static kinematic props remain obstacles. Dense physics queries fall back to complete hit lists. A disconnected nearest goal uses a reachable exterior approach. A blocked starting cell cannot jump through a wall to join a route.

Moving obstructions trigger replanning. Large combined collider bounds are restricted to the contact neighborhood so a blocked spot cannot erase the whole navigation map. The final endpoint always remains the validated drivable point; it is never replaced with an unchecked authored waypoint.

## Patrol soldier

The soldier patrols its existing route, detects the player by FOV and line of sight, uses an individually sized ground A* grid to chase around static obstacles, and remembers the last seen location for eight seconds. It fires actual rifle bursts only with clear sight within its combat behavior. If cover breaks sight, it navigates toward the last seen location rather than switching to an idle firing state. Null player/animator references are handled safely. Dynamic collision blockage produces a temporary obstacle and replan.

Patrol soldiers have 200 health: the default 25-damage rifle kills on the eighth hit. Removing the USB after the download restores all active-scene patrol soldiers (including dead soldiers with disabled AI) to full health at their original spawn poses, enables their controllers/animation, and resets their normal waypoint patrol. Elite guards are not revived by that event.

The control-room elite guard resolves the archive room's doorway and keeps facing that fixed entrance while defending, searching or repositioning. The initial door position is cached so opening the door does not make the guard follow the swinging panel. A fixed doorway marker can be assigned explicitly for other layouts.

## Opening briefing

Each fresh play session starts with a four-second "OPERATION 1989 PRESENTS" title opening, then five eight-second shots: the live drone, jeep, patrol soldier, control-room elite, and the extraction helicopter. Bottom captions explain each threat, its counter and how to steal the USB and escape. The briefing reuses the helicopter's outro music unless an explicit briefing clip is assigned.

The drone and patrol shots capture a fixed world-space camera position when their shot begins. A gentle sideways slide is limited to 0.45 m in either direction; panning and optical framing keep the moving agent in view without translating the camera along its route. Walls constrain the small slide. Jeep, elite and helicopter framing retains the previous cinematic behavior.

The entrance truck stays at its start point with its entry timer and engine audio held until the briefing ends. Space skips either the title or an agent shot into the existing truck cinematic; that key press cannot also skip the truck cinematic. A later Space press retains the truck's original skip-to-gameplay behavior. Retrying the gate sequence does not replay the briefing. `IntroTruckSequence.showAgentBriefing`, `briefingOpeningSeconds`, `briefingShotSeconds` and `briefingMusicVolume` expose the settings in the Inspector.

## Editor checks

- `Operation 1989 > Jeep > Install A* Drone Pursuit` installs the components for Inspector tuning. The runtime bootstrap also installs them automatically.
- `Validate A* Pursuit` checks actual scene routes in Edit Mode.
- `Validate Coordination Paths In Edit Mode` checks open-ground shortest cost, building detours, solid-start rejection, and fallback from a disconnected nearest goal using an isolated temporary physics layout. It writes `Temp/AgentCoordination/editmode-validation.txt` and never enters Play Mode.

`mainBuildingEntrance` can be assigned explicitly; army base defaults to `Main Building Entrance Checkpoint`. Runtime driving/scene tuning still requires a later Play Mode pass.

## Low drone positioning and tree passage

Tracking holds roughly 12 m ahead and 6 m above ground; patrol nodes use 8 m above ground. Tracking heading updates with player movement and remains stable while the player stands still to aim. Occupied tracking spots use alternate forward positions. Flight A* adds lateral detour points around tree/building colliders at the low target height, rather than relying on the former 18 m perimeter nodes.

Flight uses acceleration-limited velocity, braking into waypoints and an upright navigation root. Gentle pitch/roll is applied absolutely to the visual model rather than accumulated on the root. Hover bobbing offsets only the visual model, keeping the path position stable. Swept collision queries check the actual movement step as well as the upcoming path segment.

The RUKO FBX is already imported Y-up. The prefab, setup builder and runtime model pose use an identity rest rotation, avoiding a second 90-degree correction that stood the airframe vertically. Blade meshes spin on individual Y-up pivots at their bounds centers.

The player controller ignores only its own collision pairs with tree geometry (including leaves, roots and trunks). It reapplies these after controller resets/respawn and restores owned pairs on destruction. Tree colliders remain enabled for drone routing, other agents and raycasts; terrain support, buildings and general physics layers are unchanged. The entrance passage also leaves tree pairs to this policy.
