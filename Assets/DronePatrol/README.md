# Watchtower drone patrol

Open `Assets/army base.unity`, enter Play mode, and select **Display 3** in the Game view to follow the drone. The main player view and third person view remain on Displays 1 and 2. Audio continues to use the player's existing listener, so the drone gets quieter with distance.

`Drone Surveillance Patrol` contains the launch pad, RUKO drone, and editable survey waypoints. The pad sits above the green watchtower beside the center road. The drone waits one second, spins up for 2.5 seconds, and climbs for five seconds before starting its route.

The survey route stays inside the base perimeter, inset to X -78 to 78 and Z -82 to 74. The drone flies at Y 18 over open ground and rises over roofs and tree canopies with four units of clearance. Its speed is 6 units per second. The drone is 2.43 units wide to make it easier to see from the ground. Select `RUKO Survey Drone` to see the route gizmo and adjust speed, launch timing, audio volume, or camera placement.

`DroneFlying.mp3` supplies the takeoff opening. `DroneCruiseLoop.wav` is a seamless sustained section derived from seconds 12–32. The controller blends into it after the opening, avoiding repeated takeoff sounds. Ambient aircraft start every 60 seconds while outdoors, at volume 0.3; recordings still finish naturally.

Drone audio has a peak volume of 0.95. Its distance gain is full within 8 units, 30% at 24 units and 6% at 65 units, then reaches silence at 120 units. The truck uses the same curve with distances of 4, 12, 28, and 55 units. These are 3D distances from the active listener, including height. The custom curves clamp to zero beyond their cutoff. Helicopter passes use an additional 0.3 multiplier (effective volume 0.09) and a four-second fade-in.

Intruder detection is deferred: this drone has no `DroneSpotter` or `DroneAlertSystem` component. Later detection can be added separately from flight.

Editor checks are available at **Operation 1989 > Drone > Validate Patrol**. The installation menu is intended for a scene without an existing drone.
