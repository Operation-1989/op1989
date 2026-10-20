using UnityEngine;

/// <summary>Interior chatter and alternating, full-length outdoor aircraft recordings.</summary>
[DisallowMultipleComponent]
public sealed class MissionAmbientAudio : MonoBehaviour
{
    public Transform player;
    public IntroTruckSequence intro;
    public BoxCollider controlRoom;
    public AudioClip controlRoomChatter;
    public AudioClip[] flybyClips;
    public AudioClip helicopterFlyby;
    [Range(0, 1)] public float chatterVolume = .22f;
    [Range(0, 1)] public float flybyVolume = .55f;
    public Vector2 flybyInterval = new Vector2(30, 30);
    [Min(0)] public float helicopterFadeInSeconds = 2;
    AudioSource chatter, chatterTail, aircraft;
    double nextFlyby;
    double flybyStarted;
    float indoorBlend;
    int nextClip;
    CombatHealth health;
    bool flybyActive;

    public bool InsideControlRoom(Vector3 position)
    {
        if (!controlRoom) return false;
        Vector3 local = controlRoom.transform.InverseTransformPoint(position) - controlRoom.center;
        Vector3 half = controlRoom.size * .5f;
        return Mathf.Abs(local.x) < half.x && Mathf.Abs(local.y) < half.y && Mathf.Abs(local.z) < half.z;
    }

    public bool IsOutside(Vector3 position)
    {
        if (InsideControlRoom(position)) return false;
        // Ignore the player's own colliders; a solid ceiling suppresses passes in other buildings too.
        foreach (var hit in Physics.RaycastAll(position + Vector3.up * 1.7f, Vector3.up, 40, ~0, QueryTriggerInteraction.Ignore))
            if (!hit.transform.IsChildOf(player) && hit.normal.y < -.5f) return false;
        return true;
    }

    AudioSource MakeSource(string label)
    {
        var go = new GameObject(label);
        go.transform.SetParent(transform, false);
        var source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.volume = 0;
        // The recordings already contain the aircraft's approach, pass and departure.
        // A 2D source preserves their full stereo image and avoids camera-distance attenuation.
        source.spatialBlend = 0;
        source.dopplerLevel = 0;
        return source;
    }

    void InitializeAudio()
    {
        if (!chatter) chatter = MakeSource("Control room chatter");
        if (!chatterTail) chatterTail = MakeSource("Control room loop crossfade");
        chatter.clip = chatterTail.clip = controlRoomChatter;
        if (!aircraft) aircraft = MakeSource("Distant aircraft pass");
        if (player) health = player.GetComponent<CombatHealth>();
    }

    // This project's Play Mode options can retain scene objects and managed fields between runs.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void BeginPlaySession()
    {
        foreach (var ambience in FindObjectsByType<MissionAmbientAudio>())
            if (ambience.isActiveAndEnabled) ambience.ResetSession();
    }

    void ResetSession()
    {
        InitializeAudio();
        chatter.Stop();
        chatterTail.Stop();
        aircraft.Stop();
        flybyActive = false;
        indoorBlend = 0;
        nextClip = 0;
        nextFlyby = AudioSettings.dspTime;
    }

    // Use the audio clock: a slow frame rate or changed time scale must not stretch the gaps.
    void ScheduleFlyby() => nextFlyby = AudioSettings.dspTime + Random.Range(Mathf.Max(0, flybyInterval.x), Mathf.Max(0, Mathf.Max(flybyInterval.x, flybyInterval.y)));

    void Update()
    {
        if (!player) return;
        float dt = Time.deltaTime;
        bool missionActive = (!intro || intro.HandedOff) && (!health || !health.Dead);
        bool flybysAllowed = !health || !health.Dead;
        bool inside = InsideControlRoom(player.position);
        indoorBlend = Mathf.MoveTowards(indoorBlend, missionActive && inside ? 1 : 0, dt / 1.5f);
        UpdateChatter();

        bool outside = IsOutside(player.position);
        float fadeIn = flybyActive && aircraft.clip == helicopterFlyby && helicopterFadeInSeconds > 0
            ? Mathf.SmoothStep(0, 1, (float)(AudioSettings.dspTime - flybyStarted) / helicopterFadeInSeconds)
            : 1;
        aircraft.volume = flybyVolume * fadeIn;
        aircraft.mute = !flybysAllowed || !outside;
        // Let the AudioSource finish naturally; never truncate a recording using a frame timer.
        if (flybyActive && !aircraft.isPlaying)
        {
            flybyActive = false;
            ScheduleFlyby();
        }
        if (!flybyActive && AudioSettings.dspTime >= nextFlyby && flybysAllowed && outside)
        {
            if (flybyClips == null || flybyClips.Length == 0) { ScheduleFlyby(); return; }
            AudioClip clip = null;
            // Round-robin selection guarantees helicopter passes as well as planes.
            for (int i = 0; i < flybyClips.Length; i++)
            {
                int index = nextClip % flybyClips.Length;
                nextClip = (index + 1) % flybyClips.Length;
                if (flybyClips[index] && flybyClips[index].length > 0) { clip = flybyClips[index]; break; }
            }
            if (!clip || clip.length <= 0) { ScheduleFlyby(); return; }
            aircraft.clip = clip;
            flybyStarted = AudioSettings.dspTime;
            aircraft.volume = clip == helicopterFlyby && helicopterFadeInSeconds > 0 ? 0 : flybyVolume;
            aircraft.Play();
            flybyActive = true;
        }
    }

    void UpdateChatter()
    {
        if (!controlRoomChatter) return;
        if (indoorBlend <= 0) { chatter.Stop(); chatterTail.Stop(); return; }
        if (!chatter.isPlaying && chatterTail.isPlaying)
        {
            var finished = chatter; chatter = chatterTail; chatterTail = finished;
        }
        if (!chatter.isPlaying) chatter.Play();
        float overlap = Mathf.Min(2, controlRoomChatter.length * .2f);
        float remaining = controlRoomChatter.length - chatter.time;
        float mix = Mathf.Clamp01(1 - remaining / Mathf.Max(.01f, overlap));
        if (mix > 0 && !chatterTail.isPlaying) chatterTail.Play();
        chatter.volume = chatterVolume * indoorBlend * (1 - mix);
        chatterTail.volume = chatterVolume * indoorBlend * mix;
        if (remaining <= .05f || (!chatter.isPlaying && chatterTail.isPlaying))
        {
            chatter.Stop();
            var old = chatter; chatter = chatterTail; chatterTail = old;
        }
    }

    void OnDisable()
    {
        if (chatter) chatter.Stop();
        if (chatterTail) chatterTail.Stop();
        if (aircraft) aircraft.Stop();
        flybyActive = false;
        indoorBlend = 0;
    }

    // The first pass begins on the first outdoor frame; later passes use the random interval.
    void OnEnable() => ResetSession();
}
