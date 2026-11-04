using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Automatic pilot boarding, recorded startup, and a gate-bound cinematic extraction.</summary>
public sealed class HelicopterOutroSequence : MonoBehaviour
{
    public IntelTheftMission mission;
    public Transform helicopter, pilotEyes, mainRotor, tailRotor;
    public Renderer parkedBlades, flyingBlades, parkedTail, flyingTail;
    public AudioClip startupClip, chatterClip;
    public AudioClip outroMusic;
    [Range(0, 1)] public float outroMusicVolume = .28f;
    [Range(0, 1)] public float approachEngineVolume = .45f;
    [Range(0, 1)] public float cabinEngineVolume = .2f;
    [Range(0, 1)] public float chatterVolume = .75f;
    [Min(.1f)] public float engineHandoffSeconds = 1.2f;
    [Min(1)] public float engineFadeOutSeconds = 6;
    [Min(1)] public float visualFadeSeconds = 3.5f;
    [Min(.1f)] public float musicFadeInSeconds = 1.75f;
    [Min(0)] public float musicBlackHoldSeconds = 1;
    [Min(.1f)] public float musicFadeOutSeconds = 3;
    public Vector3 gatePosition = new Vector3(4, 0, -90);
    public Transform exteriorCameraAnchor;
    public Vector3 exteriorTravelOffset = new Vector3(14, 0, 0);
    [Range(35, 90)] public float exteriorFieldOfView = 70;
    public OutroSunsetTransition sunset;
    public HelicopterObserverViews observerViews;
    public BoxCollider engineMuffleRoom;
    [Range(0, .1f)] public float indoorEngineGain = .01f;
    [Min(1)] public float nearEngineDistance = 8;
    [Min(10)] public float engineFadeDistance = 110;
    public float ApproachVolumeScale { get; private set; }
    public bool ListenerIndoors { get; private set; }
    [Min(.25f)] public float spoolSeconds = 2;
    [Min(.25f)] public float boardingLeadSeconds = 2;
    [Min(1)] public float takeoffAudioSeconds = 55;
    [Min(0)] public float afterChatterSeconds = 3;
    public bool Running { get; private set; }
    public bool Finished { get; private set; }
    public bool ExteriorView { get; private set; }
    public float Elapsed => Running ? (float)(AudioSettings.dspTime - boardedAt) : 0;
    public float ChatterStartsAt { get; private set; }
    public float LiftOffStartsAt { get; private set; }
    public float EndsAt { get; private set; }
    public float ExteriorStartsAt => ChatterStartsAt + (chatterClip ? chatterClip.length : 0) + afterChatterSeconds;
    public float RecordingTime => !starting ? 0 : HoldingForPilot ? takeoffAudioSeconds - boardingLeadSeconds :
        Mathf.Clamp((float)(AudioSettings.dspTime - startupAt), 0, startupClip.length);
    public bool HoldingForPilot => starting && !Running && AudioSettings.dspTime >= startupAt + takeoffAudioSeconds - boardingLeadSeconds;
    public Vector3 CinematicFocus => helicopter.position + helicopter.rotation * new Vector3(0, 2.2f, -2.5f);
    public bool RotorsStarting => starting;
    public float PresentedTime { get; private set; }
    public bool LiftedInCockpit { get; private set; }
    public float FirstExteriorTime { get; private set; } = -1;
    public float RotorSpeed { get; private set; }
    public int ShotIndex { get; private set; } = -1;

    AudioSource startup, chatter, idle, music;
    double musicStartedAt = -1, musicBlackAt = -1;
    float musicTailVolume;
    float handoffStartVolume, cabinGain;
    AudioLowPassFilter startupMuffle, idleMuffle;
    float obstructionGain = 1, obstructionTarget = 1;
    float nextObstructionCheck;
    AudioClip idleClip;
    bool idleScheduled, handoffActive;
    double startupAt, boardedAt, rotorsStartedAt;
    bool initialized, starting;
    Vector3 parkedPosition, cameraLocalPosition;
    Quaternion parkedRotation, mainRotation, tailRotation, cameraLocalRotation;
    float cameraFov, cameraNear;
    bool wasMovement, wasController, wasProtected;
    FirstPersonController movement;
    CharacterController controller;
    CombatHealth health;
    Camera view;
    readonly List<Behaviour> suspended = new List<Behaviour>();
    readonly List<Renderer> hidden = new List<Renderer>();
    readonly List<Collider> disabledColliders = new List<Collider>();

    void Awake() => Initialize();
    void Initialize()
    {
        if (initialized || !helicopter || !mission || !mission.playerCamera) return;
        initialized = true;
        parkedPosition = helicopter.position; parkedRotation = helicopter.rotation;
        if (mainRotor) mainRotation = mainRotor.localRotation;
        if (tailRotor) tailRotation = tailRotor.localRotation;
        SetBlades(false);
        view = mission.playerCamera;
        movement = mission.player.GetComponent<FirstPersonController>();
        controller = mission.player.GetComponent<CharacterController>();
        health = mission.player.GetComponent<CombatHealth>();
        startup = MakeSource("Helicopter startup", startupClip, false);
        chatter = MakeSource("Helicopter radio chatter", chatterClip, false);
        idle = MakeSource("Helicopter waiting for pilot", null, true);
        music = MakeSource("Outro background music", outroMusic, true);
        music.volume = 0;
        foreach (var clip in new[] { startupClip, chatterClip, outroMusic }) if (clip) clip.LoadAudioData();
        ConfigureApproachSource(startup); ConfigureApproachSource(idle);
        startupMuffle = startup.gameObject.AddComponent<AudioLowPassFilter>();
        idleMuffle = idle.gameObject.AddComponent<AudioLowPassFilter>();
    }
    void ConfigureApproachSource(AudioSource source)
    {
        source.spatialBlend = 1; source.minDistance = nearEngineDistance; source.maxDistance = engineFadeDistance;
        source.rolloffMode = AudioRolloffMode.Custom;
        // Distance gain is applied below, with a flat spatial curve to avoid double attenuation.
        source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, AnimationCurve.Constant(0, 1, 1));
    }
    void UpdateApproachAudio(bool immediate = false)
    {
        if (!mission.player) return;
        Vector3 position = mission.player.position;
        if (immediate || Time.unscaledTime >= nextObstructionCheck)
        {
            nextObstructionCheck = Time.unscaledTime + .1f;
            ListenerIndoors = engineMuffleRoom && new Bounds(engineMuffleRoom.center, engineMuffleRoom.size)
                .Contains(engineMuffleRoom.transform.InverseTransformPoint(position));
            obstructionTarget = ListenerIndoors ? indoorEngineGain : 1;
            if (!ListenerIndoors)
            {
                Vector3 head = position + Vector3.up * 1.6f;
                Vector3 direction = helicopter.position + Vector3.up * 2 - head;
                foreach (var hit in Physics.RaycastAll(head, direction.normalized, direction.magnitude, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (hit.transform.IsChildOf(mission.player) || hit.transform.IsChildOf(helicopter)) continue;
                    obstructionTarget = .12f; break;
                }
            }
        }
        obstructionGain = immediate ? obstructionTarget : Mathf.Lerp(obstructionGain, obstructionTarget, 1 - Mathf.Exp(-Time.unscaledDeltaTime / .8f));
        float distanceGain = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(engineFadeDistance, nearEngineDistance, Vector3.Distance(position, helicopter.position)));
        float target = distanceGain * obstructionGain;
        ApproachVolumeScale = immediate ? target : Mathf.Lerp(ApproachVolumeScale, target, 1 - Mathf.Exp(-Time.unscaledDeltaTime / .35f));
        float cutoff = Mathf.Lerp(750, 22000, obstructionGain);
        if (startupMuffle) { startupMuffle.enabled = true; startupMuffle.cutoffFrequency = cutoff; }
        if (idleMuffle) { idleMuffle.enabled = true; idleMuffle.cutoffFrequency = cutoff; }
    }
    AudioSource MakeSource(string label, AudioClip clip, bool loop)
    {
        var go = new GameObject(label); go.transform.SetParent(helicopter, false);
        var source = go.AddComponent<AudioSource>(); source.playOnAwake = false;
        source.clip = clip; source.loop = loop; source.dopplerLevel = 0;
        source.spatialBlend = 0; // These are full recordings; preserve their stereo image.
        return source;
    }
    public void PrepareExtraction()
    {
        Initialize();
        if (!initialized) return;
        if (starting || Finished) return;
        UpdateApproachAudio(true);
        starting = true; rotorsStartedAt = AudioSettings.dspTime;
        // Use the running-engine part while the player returns, skipping the long windup.
        float runupOffset = Mathf.Max(0, takeoffAudioSeconds - boardingLeadSeconds - 8);
        double startsAt = AudioSettings.dspTime + .05;
        startupAt = startsAt - runupOffset;
        startup.time = runupOffset; startup.volume = approachEngineVolume * ApproachVolumeScale; startup.PlayScheduled(startsAt);
        // Stop before the takeoff cue if the player needs longer to reach the aircraft.
        startup.SetScheduledEndTime(startupAt + takeoffAudioSeconds - boardingLeadSeconds);
        ScheduleIdle();
    }
    void ScheduleIdle()
    {
        if (idleScheduled || Running || !startupClip || startupClip.loadState != AudioDataLoadState.Loaded) return;
        const float seconds = 5;
        int frames = Mathf.RoundToInt(seconds * startupClip.frequency);
        var samples = new float[frames * startupClip.channels];
        int offset = Mathf.RoundToInt((takeoffAudioSeconds - boardingLeadSeconds - seconds) * startupClip.frequency);
        if (!startupClip.GetData(samples, Mathf.Max(0, offset))) return;
        // A short edge fade keeps the repeated idle section from clicking.
        int edge = Mathf.RoundToInt(.012f * startupClip.frequency);
        for (int i = 0; i < frames; i++)
        {
            float fade = Mathf.Clamp01(Mathf.Min(i, frames - 1 - i) / (float)edge);
            for (int channel = 0; channel < startupClip.channels; channel++) samples[i * startupClip.channels + channel] *= fade;
        }
        idleClip = AudioClip.Create("Helicopter pre-takeoff idle", frames, startupClip.channels, startupClip.frequency, false);
        idleClip.SetData(samples, 0); idle.clip = idleClip; idle.volume = approachEngineVolume * ApproachVolumeScale; idle.loop = true;
        idle.PlayScheduled(System.Math.Max(AudioSettings.dspTime + .02, startupAt + takeoffAudioSeconds - boardingLeadSeconds));
        idleScheduled = true;
    }
    public bool Begin()
    {
        Initialize();
        if (!initialized || Running || Finished || !pilotEyes || !exteriorCameraAnchor || !startupClip || !chatterClip) return false;
        PrepareExtraction();
        bool wasHolding = HoldingForPilot;
        float previousCursor = RecordingTime;
        handoffStartVolume = wasHolding ? idle.volume : startup.volume;
        cabinGain = cabinEngineVolume;
        Running = true; boardedAt = AudioSettings.dspTime;
        if (startupMuffle) startupMuffle.enabled = false;
        if (idleMuffle) idleMuffle.enabled = false;
        // Fade the old engine audio under the edited takeoff lead-in, without changing pitch.
        if (!wasHolding)
        {
            idle.Stop(); idle.clip = startupClip; idle.loop = false;
            idle.time = previousCursor; idle.Play();
        }
        idle.spatialBlend = 0; idle.volume = handoffStartVolume; handoffActive = true;
        startup.Stop();
        double resumedAt = boardedAt + .05;
        startupAt = boardedAt + boardingLeadSeconds - takeoffAudioSeconds;
        startup.time = takeoffAudioSeconds - boardingLeadSeconds + .05f;
        startup.volume = 0; startup.PlayScheduled(resumedAt);
        startup.SetScheduledEndTime(startupAt + startupClip.length);
        startup.spatialBlend = 0;
        LiftOffStartsAt = Mathf.Max(0, (float)(startupAt + takeoffAudioSeconds - boardedAt));
        EndsAt = (float)(startupAt + startupClip.length - boardedAt);
        if (music && outroMusic)
        {
            music.Stop(); music.clip = outroMusic; music.time = 0; music.volume = 0;
            musicStartedAt = boardedAt + .05; musicBlackAt = -1;
            music.PlayScheduled(musicStartedAt);
        }
        cameraLocalPosition = view.transform.localPosition; cameraLocalRotation = view.transform.localRotation;
        cameraFov = view.fieldOfView; cameraNear = view.nearClipPlane;
        wasMovement = movement && movement.enabled; wasController = controller && controller.enabled;
        wasProtected = health && health.CinematicProtection;
        if (movement) movement.enabled = false;
        if (controller) controller.enabled = false;
        if (health) health.CinematicProtection = true;
        Suspend(mission.player.GetComponent<PlayerSoldierPresentation>());
        foreach (var rifle in FindObjectsByType<RifleWeapon>(FindObjectsInactive.Exclude)) Suspend(rifle);
        foreach (var ambient in FindObjectsByType<MissionAmbientAudio>(FindObjectsInactive.Exclude)) Suspend(ambient);
        foreach (var renderer in mission.player.GetComponentsInChildren<Renderer>())
            if (renderer.enabled) { hidden.Add(renderer); renderer.enabled = false; }
        foreach (var collider in helicopter.GetComponentsInChildren<Collider>())
            if (collider.enabled) { disabledColliders.Add(collider); collider.enabled = false; }
        ChatterStartsAt = LiftOffStartsAt;
        chatter.volume = chatterVolume; chatter.PlayScheduled(boardedAt + ChatterStartsAt);
        view.nearClipPlane = .025f; view.fieldOfView = 78;
        if (sunset) sunset.Begin();
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
        ApplyFlightAndCamera(0);
        return true;
    }
    void Suspend(Behaviour behaviour)
    {
        if (!behaviour || !behaviour.enabled) return;
        suspended.Add(behaviour); behaviour.enabled = false;
    }
    void Update()
    {
        Initialize();
        if (!initialized) return;
        // Music has its own audio-clock envelope and must keep updating after the visuals finish.
        UpdateMusic();
        if (!starting || Finished) return;
        if (!Running) UpdateApproachAudio();
        if (!idleScheduled && !Running) ScheduleIdle();
        float spool = Mathf.Clamp01((float)(AudioSettings.dspTime - rotorsStartedAt) / spoolSeconds);
        RotorSpeed = Mathf.SmoothStep(0, 1, spool);
        if (mainRotor) mainRotor.Rotate(Vector3.up, RotorSpeed * 1800 * Time.deltaTime, Space.World);
        // The setup creates a pivot aligned with the model's lateral rotor shaft.
        if (tailRotor) tailRotor.Rotate(Vector3.right, RotorSpeed * 2700 * Time.deltaTime, Space.Self);
        SetBlades(RotorSpeed > .7f);
        // Keep the original recording, at its original pitch, through the departure.
        float fadeProgress = Running ? Mathf.Clamp01((Elapsed - EndsAt + engineFadeOutSeconds) / engineFadeOutSeconds) : 0;
        float fadeOut = fadeProgress >= 1 ? 0 : Mathf.Pow(10, -48 * Mathf.SmoothStep(0, 1, fadeProgress) / 20);
        float blend = Running ? Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.05f, .05f + engineHandoffSeconds, Elapsed)) : 1;
        float targetCabinGain = cabinEngineVolume * (chatter.isPlaying ? .65f : 1);
        cabinGain = Mathf.Lerp(cabinGain, targetCabinGain, 1 - Mathf.Exp(-Time.unscaledDeltaTime / .8f));
        startup.volume = (Running ? cabinGain : approachEngineVolume * ApproachVolumeScale) * fadeOut * Mathf.Sin(blend * Mathf.PI * .5f);
        if (!Running) idle.volume = approachEngineVolume * ApproachVolumeScale;
        if (handoffActive)
        {
            idle.volume = handoffStartVolume * Mathf.Cos(blend * Mathf.PI * .5f);
            if (blend >= 1) { idle.Stop(); handoffActive = false; }
        }
        if (Running && Keyboard.current != null && Elapsed > 1 && Keyboard.current.escapeKey.wasPressedThisFrame) Finish();
    }
    void UpdateMusic()
    {
        if (!music || musicStartedAt < 0) return;
        double now = AudioSettings.dspTime;
        if (musicBlackAt < 0)
        {
            float fade = Mathf.SmoothStep(0, 1, Mathf.Clamp01((float)(now - musicStartedAt) / Mathf.Max(.1f, musicFadeInSeconds)));
            music.volume = outroMusicVolume * fade;
        }
        else
        {
            float afterBlack = (float)(now - musicBlackAt);
            float fade = Mathf.SmoothStep(0, 1, Mathf.Clamp01((afterBlack - musicBlackHoldSeconds) / Mathf.Max(.1f, musicFadeOutSeconds)));
            music.volume = musicTailVolume * (1 - fade);
            if (afterBlack >= musicBlackHoldSeconds + musicFadeOutSeconds)
            {
                music.Stop(); music.volume = 0; musicStartedAt = -1;
            }
        }
    }
    void SetBlades(bool flying)
    {
        if (parkedBlades) parkedBlades.enabled = !flying;
        if (flyingBlades) flyingBlades.enabled = flying;
        if (parkedTail) parkedTail.enabled = !flying;
        if (flyingTail) flyingTail.enabled = flying;
    }
    void LateUpdate()
    {
        if (!Running || Finished) return;
        ApplyFlightAndCamera(Elapsed);
        if (Elapsed >= EndsAt) Finish();
    }
    Vector3 Direction => Vector3.ProjectOnPlane(gatePosition - parkedPosition, Vector3.up).normalized;
    void ApplyFlightAndCamera(float elapsed)
    {
        PresentedTime = elapsed;
        float liftStart = LiftOffStartsAt;
        float rise = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(liftStart, liftStart + 5, elapsed));
        Vector3 hover = parkedPosition + Vector3.up * 18;
        float travel = Mathf.Clamp01((elapsed - liftStart - 5) / Mathf.Max(1, EndsAt - liftStart - 5));
        Vector3 beyond = gatePosition + Direction * 120 + Vector3.up * (parkedPosition.y + 85 - gatePosition.y);
        // A cubic path gives a gentle departure from the hover and clears the gate and buildings.
        Vector3 a = hover;
        Vector3 b = hover + Direction * 40 + Vector3.up * 8;
        Vector3 c = gatePosition + Vector3.up * (parkedPosition.y + 43 - gatePosition.y);
        float u = 1 - travel;
        Vector3 route = u*u*u*a + 3*u*u*travel*b + 3*u*travel*travel*c + travel*travel*travel*beyond;
        helicopter.position = elapsed < liftStart + 5 ? Vector3.Lerp(parkedPosition, hover, rise) : route;
        Quaternion heading = Quaternion.LookRotation(Direction, Vector3.up);
        helicopter.rotation = Quaternion.Slerp(parkedRotation, heading,
            Mathf.SmoothStep(0, 1, Mathf.InverseLerp(liftStart + 1, liftStart + 6, elapsed))) * Quaternion.Euler(travel > 0 && travel < .85f ? 3 : 0, 0, 0);
        if (sunset) sunset.SetBlend(Mathf.SmoothStep(0, 1, Mathf.InverseLerp(liftStart - 8, liftStart + 4, elapsed)));
        ExteriorView = elapsed >= ExteriorStartsAt;
        if (!ExteriorView && helicopter.position.y > parkedPosition.y + 12) LiftedInCockpit = true;
        if (ExteriorView && FirstExteriorTime < 0) FirstExteriorTime = elapsed;
        if (!ExteriorView)
        {
            view.transform.SetPositionAndRotation(pilotEyes.position, pilotEyes.rotation);
            view.fieldOfView = 78;
        }
        else
        {
            ShotIndex = 0;
            float travelAcrossRoad = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(ExteriorStartsAt, EndsAt, elapsed));
            view.transform.position = exteriorCameraAnchor.position + exteriorTravelOffset * travelAcrossRoad;
            view.transform.rotation = Quaternion.LookRotation(CinematicFocus - view.transform.position);
            view.fieldOfView = exteriorFieldOfView;
        }
    }
    void Finish()
    {
        if (!Running) return;
        Finished = true; Running = false;
        if (music && musicStartedAt >= 0)
        {
            musicBlackAt = AudioSettings.dspTime;
            musicTailVolume = music.volume;
            music.SetScheduledEndTime(musicBlackAt + musicBlackHoldSeconds + Mathf.Max(.1f, musicFadeOutSeconds));
        }
        if (startup) startup.Stop(); if (chatter) chatter.Stop(); if (idle) idle.Stop();
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        mission.CompleteExtraction();
    }
    public void ResetSequence()
    {
        if (!initialized) return;
        bool hadBoarded = Running || Finished;
        Running = Finished = ExteriorView = starting = false; ShotIndex = -1; RotorSpeed = 0;
        PresentedTime = 0; LiftedInCockpit = false; FirstExteriorTime = -1;
        if (startup) { startup.Stop(); startup.spatialBlend = 1; }
        if (chatter) chatter.Stop(); if (idle) { idle.Stop(); idle.spatialBlend = 1; }
        if (music) { music.Stop(); music.volume = 0; }
        musicStartedAt = musicBlackAt = -1; musicTailVolume = 0;
        if (idleClip) Destroy(idleClip); idleClip = null; idleScheduled = false;
        handoffActive = false;
        ApproachVolumeScale = 0;
        if (sunset) sunset.ResetLighting();
        if (helicopter) helicopter.SetPositionAndRotation(parkedPosition, parkedRotation);
        if (mainRotor) mainRotor.localRotation = mainRotation;
        if (tailRotor) tailRotor.localRotation = tailRotation;
        if (observerViews) observerViews.ResetViews();
        SetBlades(false);
        if (hadBoarded)
        {
            if (view)
            {
                view.transform.localPosition = cameraLocalPosition; view.transform.localRotation = cameraLocalRotation;
                view.fieldOfView = cameraFov; view.nearClipPlane = cameraNear;
            }
            if (health) health.CinematicProtection = wasProtected;
            if (movement) movement.enabled = wasMovement;
            if (controller) controller.enabled = wasController;
        }
        foreach (var behaviour in suspended) if (behaviour) behaviour.enabled = true;
        foreach (var renderer in hidden) if (renderer) renderer.enabled = true;
        foreach (var collider in disabledColliders) if (collider) collider.enabled = true;
        suspended.Clear(); hidden.Clear(); disabledColliders.Clear();
    }
    void OnGUI()
    {
        if (!Running && !Finished) return;
        int oldDepth = GUI.depth; GUI.depth = -500;
        Color previous = GUI.color;
        float fade = Finished ? 1 : Mathf.Max(1 - Elapsed / .6f,
            Mathf.SmoothStep(0, 1, Mathf.Clamp01((Elapsed - EndsAt + visualFadeSeconds) / visualFadeSeconds)));
        GUI.color = new Color(0, 0, 0, fade);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        if (Running && ExteriorView)
        {
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height * .07f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0, Screen.height * .93f, Screen.width, Screen.height * .07f), Texture2D.whiteTexture);
        }
        if (Running)
        {
            GUI.color = new Color(1, 1, 1, .6f);
            GUI.Label(new Rect(Screen.width - 165, Screen.height - 38, 150, 24), "ESC  Skip extraction");
        }
        GUI.color = previous; GUI.depth = oldDepth;
    }
    void OnDisable() { if (initialized) ResetSequence(); }
}
