# Base road jeep patrol

Open `Assets/army base.unity` and press Play. The textured UAZ starts on the southwest road and continuously loops clockwise along the marked perimeter: west road, north road, northeast dirt bend, east road, and the staggered south roads. The south lane leaves clearance for the entrance truck's parking place.

The scene uses the committed `Assets/3D model/UAZfbx.fbx` and its `УАЗИК.png` texture. The source assets are unchanged. A separate material adapts the model to the existing URP environment; the vehicle is scaled to 4.5 metres long and grounded at its tires.

Select `Base Jeep Patrol/Patrol Jeep (UAZ)` to change Speed (5 m/s), Acceleration (2 m/s²), Turn Speed (100 degrees/s), or obstacle clearance (0.6 m). Its kinematic Rigidbody and box collider move in FixedUpdate. The patrol stops for colliders in its path and resumes once they clear. It does not chase the player or modify other vehicles, agents, or cameras.

The 91 waypoints live under `Base Jeep Patrol/Perimeter Road Route`. Select the vehicle to see the closed red route gizmo. Move these points in Edit mode to adjust the route. `Operation 1989 > Jeep > Refresh Perimeter Waypoints` restores the authored route and overwrites manual waypoint positions.

`Operation 1989 > Jeep > Validate Road Patrol` simulates two laps in Edit mode, temporarily places the intro truck at its parking destination, checks every waypoint, and verifies obstacle stop/resume and replay reset. It restores the test transforms afterward. `Smoke Test Play Mode` checks actual Rigidbody movement and Display 1 camera assignments, then returns to Edit mode. Reports and screenshots are written to `Temp/JeepPatrol`.

`Install Base Road Patrol` is a one-time scene setup command and rejects duplicate patrols. Importing the scripts alone does not install or modify scene objects.
