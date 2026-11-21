# Tactical Recon-Hunter Drone: Autonomous Aerial Agent

**Module:** SE3062 – Intelligent Systems (Joint Project with Graphics and Visualization)  
**Role:** Student 2 (Systems Engineer)  
**Architecture:** Multi-Modal Sense-Think-Act Agent (Knowledge Base + Forward Chaining Inference Engine + 3D $A^*$ Pathfinding with Dynamic Re-Planning)

---

## 1. System Overview & Architecture

The Tactical Recon-Hunter Drone is an autonomous airborne security agent patrolling the base perimeter. It operates outside the command building, maintaining security through visual searchlights, acoustic detection, and cooperative alarm broadcasting.

```
                      ┌────────────────────────────────────────────────────────┐
                      │            Tactical Recon Drone (Sense-Think-Act)      │
                      └───────────────────────────┬────────────────────────────┘
                                                  │
          ┌───────────────────────────────────────┼───────────────────────────────────────┐
          ▼                                       ▼                                       ▼
┌──────────────────────────┐            ┌───────────────────────────┐           ┌───────────────────────────┐
│         1. SENSE         │            │          2. THINK         │           │          3. ACT           │
│       (Perception)       │            │     (Inference Engine)    │           │    (Navigation & Combat)  │
└─────────────┬────────────┘            └─────────────┬─────────────┘           └─────────────┬─────────────┘
              │                                       │                                       │
 • Dual-Cone Optical Raycasts           • Knowledge Base (KB)                   • 3D Waypoint A* Search
 • Acoustic Gunfire Listener              Assert & Retract Facts                  f(n) = g(n) + h(n)
 • Barricade & Cover Occlusion          • Forward Chaining Evaluation           • Dynamic Edge Re-Planning
 • Temporal Memory Confidence (LKP)       Production Rules (Modus Ponens)       • Suppression Bursts & Alert
```

---

## 2. Component Responsibilities

| Component | Responsibility |
| :--- | :--- |
| `TacticalDronePerception` | Handles multi-modal sensing: dual-cone optical raycasting (forward + downward cone), acoustic gunfire detection via static events, physical cover occlusion behind `PushableBarricade`, stealth detection (`SupplyTruckStealthZone`), and temporal memory confidence decay for the Last Known Position (LKP). |
| `KnowledgeBase` | Centralized blackboard / knowledge repository storing discrete environmental facts (`Fact.PlayerSpotted`, `Fact.PlayerInCover`, `Fact.GunshotHeard`, `Fact.TargetLost`). |
| `TacticalDroneInferenceEngine` | Evaluates production rules using **Forward Chaining** (data-driven reasoning). Applies Modus Ponens to derive tactical decisions (`InferredAction`). |
| `TacticalDroneNavigationGraph` | Maintains a 3D aerial waypoint graph. Implements textbook **$A^*$ Search** with Euclidean distance heuristic ($h(n)$) and handles dynamic edge invalidation. |
| `TacticalDroneMovement` | Smooth 3D flight locomotion along $A^*$ paths. Computes aerodynamic banking (roll into turns), rotor spin, forward whisker obstacle collision checks, and hover bobbing. |
| `TacticalDroneCombat` | Manages multi-state searchlight (Cyan = Patrol, Amber = Investigate, Red = Hostile Lock), hitscan suppressive firing, and alerts the Military Jeep pursuit unit via `DroneAlertSystem`. |
| `TacticalDroneController` | Master agent coordinator orchestrating the Sense-Think-Act cycle, re-planning upon obstacle detection, and rendering live telemetry overlay for the VIVA. |

---

## 3. Algorithmic Specifications & Mathematical Formulations

### A. Forward Chaining Inference (THINK)

The agent utilizes **First-Order Production Rules** evaluated via Forward Chaining:

$$\text{Facts in Knowledge Base} \implies \text{Rule Evaluation} \implies \text{Inferred Action}$$

1. **Rule 1: Direct Combat Engagement**
   $$\text{PlayerSpotted} \land \neg\text{PlayerInCover} \land \text{PlayerInShootingRange} \implies \text{Action.EngageRangedCombat}$$
   *Meaning:* Player is in line of sight, unshielded, and within weapon range. The drone halts, turns searchlight red, and opens fire.

2. **Rule 2: Flank / Reposition to Overcome Cover**
   $$\text{PlayerSpotted} \land \text{PlayerInCover} \implies \text{Action.InvestigateLastKnownPos}$$
   *Meaning:* Player is hiding behind physical barricades. The drone navigates to a new aerial angle to establish line of sight.

3. **Rule 3: Acoustic Gunfire Intercept**
   $$\text{GunshotHeard} \land \neg\text{PlayerSpotted} \implies \text{Action.InvestigateLastKnownPos}$$
   *Meaning:* Gunfire detected within acoustic hearing range ($r \le 70\text{m}$). Drone redirects path to investigate sound origin.

4. **Rule 4: Dynamic Lost Target Search**
   $$\text{TargetLost} \land (C_{\text{memory}} > 0) \implies \text{Action.InvestigateLastKnownPos}$$
   *Meaning:* Visual was lost behind an obstacle. Drone proceeds to Last Known Position (LKP) and conducts an aerial sector search.

5. **Rule 5: Baseline Perimeter Patrol**
   $$\text{Otherwise} \implies \text{Action.PatrolWaypoints}$$
   *Meaning:* No active threats. Drone autonomously patrols base boundary flight nodes.

---

### B. 3D $A^*$ Pathfinding & Heuristics (ACT)

The navigation system solves pathfinding in a continuous 3D airspace using the **$A^*$ Search Algorithm**:

$$f(n) = g(n) + h(n)$$

Where:
- $g(n)$: Actual accumulated Euclidean flight distance from start node to node $n$.
- $h(n)$: Estimated cost from node $n$ to goal node using **3D Euclidean Distance**:

$$h(n) = \sqrt{(x_{\text{goal}} - x_n)^2 + (y_{\text{goal}} - y_n)^2 + (z_{\text{goal}} - z_n)^2}$$

#### Formal Proof of Heuristic Properties:
1. **Admissibility ($h(n) \le h^*(n)$):**  
   In 3D Euclidean space, the straight-line distance is the shortest possible path between two points. Any realistic physical path with obstacles will be $\ge$ straight-line distance. Therefore, $h(n)$ **never overestimates** the true cost $h^*(n)$.
2. **Consistency / Monotonicity ($h(n) \le c(n, a, n') + h(n')$):**  
   By the geometric **Triangle Inequality**, for any three nodes $A$, $B$, and $\text{Goal}$:
   $$\text{Distance}(A, \text{Goal}) \le \text{Distance}(A, B) + \text{Distance}(B, \text{Goal})$$
   Since $c(A, B) = \text{Distance}(A, B)$, consistency is mathematically guaranteed.

---

### C. Dynamic Obstacle Avoidance & Re-Planning

1. While traversing an $A^*$ path, the drone casts a forward **sphere whisker**:
   $$\text{Raycast}(\vec{p}_{\text{drone}}, \vec{v}_{\text{forward}}, d = 6.0\text{m})$$
2. If a dynamic barrier or closing gate obstructs the active edge:
   - The edge $(n_i \leftrightarrow n_j)$ is marked **blocked** ($c_{ij} = \infty$).
   - The drone halts current segment traversal.
   - An immediate dynamic $A^*$ re-plan is computed from the drone's nearest node to the destination, discovering an alternate detour route.

---

### D. Temporal Memory & Confidence Decay

When visual contact is lost, the agent maintains an internal representation of the threat:

$$C(t) = \max\left(0, 1 - \frac{t - t_{\text{last\_seen}}}{T_{\text{memory}}}\right)$$

- At $t = t_{\text{last\_seen}}$, $C = 1.0$ (High confidence).
- As time elapses up to $T_{\text{memory}} = 8.0\text{s}$, confidence decays linearly.
- Once $C = 0$, the fact `Fact.TargetLost` is retracted, and the agent returns to routine perimeter patrol.

---

## 4. Setup, Test Scene & Verification

### Editor Menu Commands (`Operation 1989 > Tactical Drone`):
1. **1 Build Isolated Test Scene**: Creates `Assets/Scenes/TacticalDroneTest.unity` and `Assets/Prefabs/Agents/TacticalDrone.prefab`. Builds an aerial testing ground with ground plane, dynamic barricades, test player, and 3D flight nodes.
2. **2 Integrate Into Military Base**: Safely instantiates the tactical drone into `Assets/army base.unity` above the perimeter watchtowers.
3. **3 Validate In Play Mode**: Executes automated algorithmic and physical test assertions, writing the verification report to `Temp/TacticalDrone/validation.txt`.

---

## 5. VIVA Defense Cheat-Sheet

### Question 1: *"What is your agent and why did you choose this architecture?"*
**Answer:**  
> *"I built the **Autonomous Tactical Recon Drone** as Student 2 (Systems Engineer). I chose a **Forward Chaining Inference Engine** integrated with a **Knowledge Base** coupled to **3D $A^*$ Pathfinding**. This separates the Sense-Think-Act layers cleanly: sensors assert discrete facts into the Knowledge Base, production rules evaluate those facts using Modus Ponens to decide tactical actions, and the 3D $A^*$ graph executes smooth flight paths with dynamic re-planning when obstacles arise."*

### Question 2: *"Why is Forward Chaining better than a basic Finite State Machine (FSM) here?"*
**Answer:**  
> *"A standard FSM suffers from transition explosion ($O(N^2)$ transitions) when integrating multi-modal sensory inputs (visual sight, physical cover, acoustic gunshots, and decaying memory). In Forward Chaining, reasoning is modular and data-driven: when a new fact like `Fact.GunshotHeard` is asserted by the acoustic sensor, Rule 3 immediately fires and updates the action without needing dozens of cross-state transition wires."*

### Question 3: *"Can you prove your $A^*$ heuristic is admissible and consistent?"*
**Answer:**  
> *"Yes. We use 3D Euclidean distance $h(n) = \sqrt{\Delta x^2 + \Delta y^2 + \Delta z^2}$. In geometry, the straight-line distance is the absolute lower bound between any two spatial points. Since no path can ever be shorter than a straight line, $h(n) \le h^*(n)$ (Admissibility). By the Triangle Inequality, the distance directly to the goal is always less than or equal to visiting an intermediate neighbor first and then proceeding to the goal, proving $h(n) \le c(n, n') + h(n')$ (Consistency/Monotonicity)."*

### Question 4: *"How does your agent handle edge cases like players hiding behind barricades?"*
**Answer:**  
> *"When a player crouches behind a `PushableBarricade`, optical raycasts detect the obstruction. The perception system asserts `Fact.PlayerInCover`. Instead of falsely assuming the player disappeared, Forward Chaining fires **Rule 2 (Cover Awareness Flank)**, causing the drone to re-plan an aerial detour to gain higher altitude or flank around the cover."*

### Question 5: *"How does dynamic re-planning work?"*
**Answer:**  
> *"The drone continuously projects a forward sphere whisker. If an obstacle dynamically blocks the flight path between two nodes, the agent marks that edge invalid ($c = \infty$) and re-invokes $A^*$ from its current nearest node, computing an alternate detour around the obstacle in real-time."*
