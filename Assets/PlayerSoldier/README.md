# Player soldier

`Assets/army base.unity` keeps the original `Intro Player` CharacterController and FirstPersonController. Its capsule renderer is disabled. `Soldier Visual` uses the existing LowPolySoldiers demo rig, uniform, and rifle textures, without patrol AI or root motion. The original soldier prefab and NPC animation controller are unchanged.

Controls: WASD to walk, left Shift to run, left mouse button to play the rifle fire animation. Fire is visual only; this change does not implement shooting damage or projectiles. Escape releases the cursor. The intro still locks movement and looking until its camera handoff, and R retries a missed entry.

`PlayerSoldierPresentation` on `Intro Player` exposes Walk Speed (4), Run Speed (7), and Fire Interval (0.18 seconds). It drives animation from actual horizontal displacement so colliding with a wall returns to idle. The player-specific controller blends the existing idle, rifle walk, and combat run clips; the fire clip overrides only the spine and upper body, allowing the legs to keep moving. These are all four animation clips supplied locally; there are no jump, crouch, reload, or death clips.

The full textured mesh appears during the aerial shot. During first-person play, a generated copy omits the head triangles to avoid clipping into the camera while keeping the body and rifle visible when looking down. The imported mesh remains unchanged. The existing camera is aligned to the sampled idle pose and uses a 0.04 near clip plane. No cameras or AudioListeners were added.

The dedicated entrance truck uses new URP materials and locally generated tileable paint and rubber textures in `Assets/IntroTruck/Materials`. Wheel mesh copies separate rubber from painted hubs. The FBX, vehicle route, collision geometry, and patrolling jeep remain unchanged. No assets were downloaded and no packages were added.

Editor tools under `Operation 1989 > Player` inspect the source assets, capture isolated previews, and run an explicit Play Mode smoke test. The build menu is a one-time setup operation and refuses to duplicate an existing soldier. Validation reports and screenshots are written to `Temp/PlayerSoldier`.

Validation on 2026-10-17: Unity compilation succeeded, all 16 soldier checks passed, and all 23 intro regression checks passed with the new walking speed. Tests cover animation clip selection, sprint speed, fire state and recovery, camera handoff, headless mesh selection, one AudioListener, truck texture assignments, actual keyboard-driven entry, parking, gate interlocks, retry, and replay. Reports and previews are preserved in `CleanupAudit/PlayerSoldier`. Unity was left in Edit mode with `army base` saved and no console errors. The original FirstPersonController source, PatrolSoldier prefab, and NPC animation controller match their pre-change hashes.

Manual review remains appropriate for mouse sensitivity, animation appearance during strafing/backward movement, and extreme camera angles. There are no directional strafe/backward animation clips in the available set, so movement uses the existing rifle walk/run cycles.

Changed assets: `Assets/army base.unity`, `Assets/Scripts/Agents/PlayerSoldierPresentation.cs`, the two `PlayerSoldier` editor utilities under `Assets/Scripts/Editor`, this folder's controller/mask/first-person mesh, and the truck materials/textures/wheel mesh copies under `Assets/IntroTruck/Materials`, with Unity metadata. `Assets/IntroTruck/README.md` now describes the soldier visual in place of the old placeholder.
