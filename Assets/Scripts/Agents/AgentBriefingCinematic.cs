using System;
using System.Collections.Generic;
using UnityEngine;
using Operation1989.AI.Drone;
using Operation1989.AI.Jeep;

/// <summary>A short tour of the live base before the entrance truck starts.</summary>
public sealed class AgentBriefingCinematic : IDisposable
{
    sealed class Shot
    {
        public Transform subject;
        public string title, location, description;
        public Vector3 direction;
        public float minimumDistance, maximumDistance;
        public bool fixedObserver;
    }
    readonly List<Shot> shots = new List<Shot>();
    Camera view;
    AudioSource music;
    float elapsed, shotSeconds, openingSeconds, previousNear, previousFov;
    int framedShot = -1;
    float framingAngle;
    Vector3 observerAnchor, observerAimOffset, observerFocus, observerFocusVelocity;
    float observerRadius, observerUpdatedAt, observerFovVelocity;
    GUIStyle eyebrow, title, caption, skip, openingTitle, presents, openingTagline;
    public bool Active { get; private set; }
    public int ShotCount => shots.Count;
    public bool ShowingOpening => Active && elapsed < openingSeconds;
    public float OpeningDuration => openingSeconds;
    public float ShotDuration => shotSeconds;
    public int ShotIndex => shots.Count == 0 || elapsed < openingSeconds ? -1 :
        Mathf.Min(shots.Count - 1, Mathf.FloorToInt((elapsed - openingSeconds) / shotSeconds));
    public float Duration => openingSeconds + shots.Count * shotSeconds;
    public AudioClip MusicClip { get; private set; }
    public string CurrentTitle => ShotIndex < 0 ? "OPERATION 1989" : shots[ShotIndex].title;

    public bool Begin(IntroTruckSequence intro, Camera presentationCamera = null)
    {
        Stop(); shots.Clear();
        view = presentationCamera ? presentationCamera : intro.introCamera;
        if (!view) return false;
        shotSeconds = Mathf.Max(8, intro.briefingShotSeconds);
        openingSeconds = Mathf.Max(2, intro.briefingOpeningSeconds);
        var scene = intro.gameObject.scene;
        TacticalDroneController drone = null;
        BaseRoadJeepPatrol jeep = null;
        BarricadePatrolSoldier patrol = null;
        EliteGuardController elite = null;
        foreach (var item in UnityEngine.Object.FindObjectsByType<TacticalDroneController>())
            if (item.gameObject.scene == scene && item.isActiveAndEnabled) { drone = item; break; }
        foreach (var item in UnityEngine.Object.FindObjectsByType<BaseRoadJeepPatrol>())
            if (item.gameObject.scene == scene) { jeep = item; break; } // Its A* driver disables the legacy component.
        foreach (var item in UnityEngine.Object.FindObjectsByType<BarricadePatrolSoldier>())
            if (item.gameObject.scene == scene) { patrol = item; break; }
        foreach (var item in UnityEngine.Object.FindObjectsByType<EliteGuardController>())
            if (item.gameObject.scene == scene) { elite = item; break; }
        Add(drone ? drone.transform : null, "RECON DRONE", "BASE AIRSPACE",
            "It reports your position to the jeep. Shoot it down to stop the reports.", 6, 12, .95f, true);
        Add(jeep ? jeep.transform : null, "PATROL JEEP", "PERIMETER ROADS",
            "It spots intruders and blocks the main building. Use cover, or land six hits to stall it for 30 seconds.", 7, 18);
        Add(patrol ? patrol.transform : null, "PATROL SOLDIER", "CHECKPOINT PATROL",
            "Stay behind cover and land eight rifle hits. The patrol returns when you take the secret.", 6, 10, .45f, true);
        Add(elite ? elite.transform : null, "ELITE SOLDIER", "CONTROL ROOM",
            "The elite watches the doorway. Use the room's cover to defeat this guard and reach the computer.", 3, 6);
        // Existing small test/utility scenes without agents retain their original truck intro.
        if (shots.Count == 0) return false;
        HelicopterOutroSequence outro = intro.intelMission ? intro.intelMission.outro : null;
        if (!outro)
            foreach (var item in UnityEngine.Object.FindObjectsByType<HelicopterOutroSequence>())
                if (item.gameObject.scene == scene) { outro = item; break; }
        Transform extraction = outro && outro.helicopter ? outro.helicopter :
            intro.intelMission && intro.intelMission.extraction ? intro.intelMission.extraction.transform : intro.truck;
        Add(extraction, "STEAL THE SECRET. GET OUT.", "MISSION OBJECTIVE",
            "Press F at the computer to copy the secret and take the USB. Follow the compass to the helipad and board to win.", 12, 35);
        MusicClip = intro.briefingMusic ? intro.briefingMusic : outro ? outro.outroMusic : null;
        previousNear = view.nearClipPlane; previousFov = view.fieldOfView; view.nearClipPlane = .08f;
        elapsed = 0; framedShot = -1; Active = true;
        if (Application.isPlaying && MusicClip)
        {
            if (!music)
            {
                var audioObject = new GameObject("Agent briefing music");
                audioObject.transform.SetParent(intro.transform, false);
                music = audioObject.AddComponent<AudioSource>();
                music.playOnAwake = false; music.spatialBlend = 0; music.loop = true;
            }
            music.clip = MusicClip; music.volume = 0; music.Play();
        }
        ApplyCamera();
        return true;
    }

    void Add(Transform subject, string heading, string location, string description, float minimum, float maximum,
        float elevation = .45f, bool fixedObserver = false)
    {
        if (!subject) return;
        Vector3 forward = Vector3.ProjectOnPlane(subject.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < .01f) forward = Vector3.forward;
        shots.Add(new Shot { subject = subject, title = heading, location = location, description = description,
            direction = (forward + Vector3.Cross(Vector3.up, forward) * .8f + Vector3.up * elevation).normalized,
            minimumDistance = minimum, maximumDistance = maximum, fixedObserver = fixedObserver });
    }

    /// <returns>True once this briefing finishes; the caller starts the truck on that frame.</returns>
    public bool Tick(float unscaledDeltaTime, bool skipPressed, float volume)
    {
        if (!Active || GamePauseMenu.BlocksInput) return false;
        elapsed += Mathf.Max(0, unscaledDeltaTime);
        if (skipPressed || elapsed >= Duration) { Stop(); return true; }
        if (music)
        {
            float fade = Mathf.Min(Mathf.Clamp01(elapsed / 1.1f), Mathf.Clamp01((Duration - elapsed) / 1.2f));
            music.volume = Mathf.Clamp01(volume) * Mathf.SmoothStep(0, 1, fade);
        }
        return false;
    }

    static Bounds SubjectBounds(Transform subject)
    {
        Bounds bounds = new Bounds(subject.position + Vector3.up, Vector3.one);
        bool first = true;
        foreach (var renderer in subject.GetComponentsInChildren<Renderer>())
        {
            if ((!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) ||
                !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            if (first) { bounds = renderer.bounds; first = false; }
            else bounds.Encapsulate(renderer.bounds);
        }
        return bounds;
    }

    public void ApplyCamera()
    {
        if (!Active || !view || ShotIndex < 0 || GamePauseMenu.BlocksInput) return;
        var shot = shots[ShotIndex];
        if (!shot.subject) return;
        float t = Mathf.Clamp01((elapsed - openingSeconds - ShotIndex * shotSeconds) / shotSeconds);
        bool newShot = framedShot != ShotIndex;
        if (shot.fixedObserver && !newShot)
        {
            ApplyObserverCamera(shot, false);
            return;
        }
        Bounds bounds = SubjectBounds(shot.subject);
        Vector3 focus = bounds.center;
        Vector3 direction = Quaternion.Euler(0, Mathf.Lerp(-5, 5, Mathf.SmoothStep(0, 1, t)), 0) * shot.direction;
        float radius = Mathf.Max(bounds.extents.y, bounds.extents.x, bounds.extents.z);
        float distance = Mathf.Clamp(radius * 3.5f, shot.minimumDistance, shot.maximumDistance) * Mathf.Lerp(1.06f, 1, t);
        Vector3 selected = focus + direction * distance;
        float bestClearance = -1;
        // Find a real camera position inside the room/base geometry, not through a wall.
        if (newShot) framingAngle = 0;
        foreach (float angle in new[] { framingAngle, 0f, 35f, -35f, 70f, -70f, 120f, -120f, 180f })
        {
            Vector3 ray = Quaternion.Euler(0, angle, 0) * direction;
            float clear = distance;
            foreach (var hit in Physics.SphereCastAll(focus, .15f, ray, distance, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform == shot.subject || hit.transform.IsChildOf(shot.subject)) continue;
                clear = Mathf.Min(clear, Mathf.Max(.3f, hit.distance - .2f));
            }
            if (clear > bestClearance)
            { bestClearance = clear; selected = focus + ray * clear; framingAngle = angle; }
            if (clear >= distance * .98f) break;
        }
        if (shot.fixedObserver)
        {
            // Capture a world-space camera position once. Subject motion must not
            // translate the camera for the rest of this shot.
            observerAnchor = selected;
            // Sample the composition once. Rotor/walk animation bounds must not
            // drive the camera's aim or zoom on subsequent frames.
            observerAimOffset = focus - shot.subject.position;
            observerRadius = Mathf.Max(.2f, bounds.extents.x, bounds.extents.y, bounds.extents.z);
            framedShot = ShotIndex;
            ApplyObserverCamera(shot, true);
            return;
        }
        view.fieldOfView = 52;
        if (!newShot)
        {
            selected = Vector3.Lerp(view.transform.position, selected, 1 - Mathf.Exp(-6f * Time.unscaledDeltaTime));
            Vector3 cameraRay = selected - focus;
            float clearance = cameraRay.magnitude;
            foreach (var hit in Physics.SphereCastAll(focus, .15f, cameraRay.normalized, cameraRay.magnitude, ~0, QueryTriggerInteraction.Ignore))
                if (hit.transform != shot.subject && !hit.transform.IsChildOf(shot.subject))
                    clearance = Mathf.Min(clearance, Mathf.Max(.3f, hit.distance - .2f));
            selected = focus + cameraRay.normalized * clearance;
        }
        framedShot = ShotIndex;
        view.transform.SetPositionAndRotation(selected, Quaternion.LookRotation(focus - selected, Vector3.up));
    }

    void ApplyObserverCamera(Shot shot, bool firstFrame)
    {
        Vector3 target = shot.subject.position + observerAimOffset;
        // Use briefing time so repeated ApplyCamera calls in the same frame
        // cannot advance the filter. Each new shot starts with fresh damping.
        float delta = Mathf.Max(0, elapsed - observerUpdatedAt);
        if (firstFrame)
        {
            observerFocus = target;
            observerFocusVelocity = Vector3.zero;
            observerFovVelocity = 0;
        }
        else if (delta > 0)
            observerFocus = Vector3.SmoothDamp(observerFocus, target, ref observerFocusVelocity,
                .5f, Mathf.Infinity, delta);
        observerUpdatedAt = elapsed;
        Vector3 look = observerFocus - observerAnchor;
        if (look.sqrMagnitude < .01f) return;
        float framing = Mathf.Clamp(2 * Mathf.Atan(observerRadius * 2.2f / look.magnitude) * Mathf.Rad2Deg, 26, 60);
        if (firstFrame) view.fieldOfView = framing;
        else if (delta > 0)
            view.fieldOfView = Mathf.SmoothDamp(view.fieldOfView, framing, ref observerFovVelocity,
                .8f, 12, delta);
        view.transform.SetPositionAndRotation(observerAnchor, Quaternion.LookRotation(look, Vector3.up));
    }

    public void Draw()
    {
        if (!Active) return;
        if (caption == null)
        {
            eyebrow = MissionHudStyle.Label(MissionHudStyle.Semibold, 12, MissionHudStyle.Accent);
            title = MissionHudStyle.Label(MissionHudStyle.Semibold, 26, Color.white);
            caption = MissionHudStyle.Label(MissionHudStyle.Regular, 18, new Color(.9f, .92f, .88f));
            skip = MissionHudStyle.Label(MissionHudStyle.Semibold, 13, new Color(.85f, .88f, .84f));
            skip.alignment = TextAnchor.MiddleRight;
            openingTitle = MissionHudStyle.Label(MissionHudStyle.Semibold, 72, new Color(.96f, .95f, .89f));
            presents = MissionHudStyle.Label(MissionHudStyle.Semibold, 18, MissionHudStyle.Accent);
            openingTagline = MissionHudStyle.Label(MissionHudStyle.Regular, 16, new Color(.7f, .76f, .71f));
            openingTitle.alignment = presents.alignment = openingTagline.alignment = TextAnchor.MiddleCenter;
        }
        var matrix = GUI.matrix;
        var color = GUI.color;
        int depth = GUI.depth;
        GUI.depth = -500; GUI.color = Color.white;
        float scale = MissionHudStyle.Scale, width = Screen.width / scale, height = Screen.height / scale;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * scale);
        if (ShowingOpening)
        {
            MissionHudStyle.Fill(new Rect(0, 0, width, height), Color.black);
            float fade = Mathf.SmoothStep(0, 1, Mathf.Min(Mathf.Clamp01(elapsed / .9f), Mathf.Clamp01((openingSeconds - elapsed) / .7f)));
            GUI.color = new Color(1, 1, 1, fade);
            openingTitle.fontSize = Mathf.RoundToInt(Mathf.Lerp(70, 74, elapsed / openingSeconds));
            GUI.Label(new Rect(40, height * .31f, width - 80, 112), "OPERATION 1989", openingTitle);
            GUI.Label(new Rect(40, height * .5f, width - 80, 32), "P R E S E N T S", presents);
            MissionHudStyle.Fill(new Rect(width * .5f - 50, height * .59f, 100, 2), new Color(.76f, .69f, .42f, fade));
            GUI.Label(new Rect(40, height * .65f, width - 80, 34), "Know your enemy. Recover the secret. Escape.", openingTagline);
            GUI.color = Color.white;
        }
        else
        {
        MissionHudStyle.Fill(new Rect(0, 0, width, 46), new Color(.01f, .02f, .015f, .94f));
        MissionHudStyle.Fill(new Rect(0, height - 174, width, 174), new Color(.01f, .02f, .015f, .94f));
        var shot = shots[ShotIndex];
        GUI.Label(new Rect(152, 13, 460, 22), "OPERATION 1989  /  FIELD BRIEFING", eyebrow);
        float bottom = height - 151;
        GUI.Label(new Rect(42, bottom, width - 84, 22), $"{ShotIndex + 1:00} / {ShotCount:00}   {shot.location}", eyebrow);
        GUI.Label(new Rect(42, bottom + 26, width - 84, 36), shot.title, title);
        GUI.Label(new Rect(42, bottom + 68, width - 84, 60), shot.description, caption);
        float shotTime = elapsed - openingSeconds - ShotIndex * shotSeconds;
        float black = Mathf.Max(1 - Mathf.Clamp01(shotTime / .35f), 1 - Mathf.Clamp01((shotSeconds - shotTime) / .4f));
        if (black > 0) MissionHudStyle.Fill(new Rect(0, 0, width, height), new Color(0, 0, 0, black));
        }
        GUI.Label(new Rect(width - 290, 9, 248, 28), "SPACE  Skip briefing", skip);
        GUI.matrix = matrix; GUI.color = color; GUI.depth = depth;
    }

    public void Stop()
    {
        if (Active && view) { view.nearClipPlane = previousNear; view.fieldOfView = previousFov; }
        Active = false;
        if (music) { music.Stop(); music.volume = 0; }
    }
    public void Dispose()
    {
        Stop();
        if (music) UnityEngine.Object.Destroy(music.gameObject);
    }
}
