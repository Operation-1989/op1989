using UnityEngine;
using UnityEngine.InputSystem;

public sealed class CombatHealth : MonoBehaviour
{
    public float maximumHealth=100;
    public bool isPlayer;
    [Min(.1f)] public float respawnDelay = 10;
    [Header("Player health regeneration")]
    public bool regeneratePlayerHealth = true;
    [Tooltip("Damage-free delay before healing starts. Uses gameplay time.")]
    [Min(0)] public float regenerationDelay = 3;
    [Tooltip("Time to refill the health missing after the last hit. Default total recovery: 3 + 12 = 15 seconds.")]
    [Min(.1f)] public float regenerationDuration = 12;
    public float Current {get;private set;}
    public bool Dead=>Current<=0;
    public bool CinematicProtection { get; set; }
    public bool IsRegenerating => isPlayer && regeneratePlayerHealth && !Dead && Current < maximumHealth &&
        !float.IsNegativeInfinity(lastDamagedAt) && Time.time >= lastDamagedAt + Mathf.Max(0, regenerationDelay);
    float hurtUntil;
    float respawnAt;
    float lastDamagedAt = float.NegativeInfinity;
    float regenerationStartHealth;
    readonly CombatHud hud = new CombatHud();
    Vector3 spawn;
    Quaternion rotation;
    void Awake(){Current=maximumHealth;spawn=transform.position;rotation=transform.rotation;}
    public void TakeDamage(float amount)
    {
        if(Dead || CinematicProtection || amount<=0)return;
        Current=Mathf.Max(0,Current-amount);hurtUntil=Time.time+.18f;
        lastDamagedAt=Time.time;regenerationStartHealth=Current;
        var guard=GetComponent<BarricadePatrolSoldier>();if(guard)guard.Alert();
        if(!Dead)return;
        if(isPlayer) {FindAnyObjectByType<IntelTheftMission>()?.ResetForCheckpoint();respawnAt=Time.unscaledTime+respawnDelay;GetComponent<FirstPersonController>().enabled=false;Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}
        else
        {
            if(guard)guard.enabled=false;
            var controller=GetComponent<CharacterController>();if(controller)controller.enabled=false;
            var animator=GetComponentInChildren<Animator>();if(animator)animator.enabled=false;
            transform.rotation=rotation*Quaternion.Euler(0,0,85);
        }
    }
    public void Restore()
    {
        Current=maximumHealth;
        hurtUntil=0;
        lastDamagedAt=float.NegativeInfinity;regenerationStartHealth=Current;
        if(!isPlayer)
        {
            var controller=GetComponent<CharacterController>();if(controller)controller.enabled=false;
            transform.SetPositionAndRotation(spawn,rotation);
            if(controller)controller.enabled=true;
            var animator=GetComponentInChildren<Animator>();if(animator)animator.enabled=true;
            var guard=GetComponent<BarricadePatrolSoldier>();if(guard){guard.ResetPatrol();guard.enabled=true;}
        }
    }
    void Update()
    {
        if(!isPlayer)return;
        if(Dead)
        {
            if(Time.unscaledTime>=respawnAt || (Keyboard.current!=null && Keyboard.current.rKey.wasPressedThisFrame))Respawn();
            return;
        }
        if(IsRegenerating)
        {
            float elapsed=Time.time-lastDamagedAt-Mathf.Max(0,regenerationDelay);
            // Absolute gameplay time avoids frame-rate dependent accumulation and pauses with the game.
            float recovered=Mathf.Lerp(regenerationStartHealth,maximumHealth,elapsed/Mathf.Max(.1f,regenerationDuration));
            Current=Mathf.Min(maximumHealth,Mathf.Max(Current,recovered));
        }
    }
    public void Respawn()
    {
        if(!isPlayer || !Dead)return;
        Restore();
        var intro=FindAnyObjectByType<IntroTruckSequence>();
        if(intro)intro.RespawnAtCurrentCheckpoint();
        else
        {
            var cc=GetComponent<CharacterController>();if(cc)cc.enabled=false;
            var checkpoint=GetComponent<PlayerCheckpointState>();
            transform.SetPositionAndRotation(checkpoint && checkpoint.HasCheckpoint ? checkpoint.RespawnPosition : spawn,
                checkpoint && checkpoint.HasCheckpoint ? checkpoint.RespawnRotation : rotation);
            if(cc)cc.enabled=true;
            GetComponent<FirstPersonController>().ResetLook();
            GetComponent<FirstPersonController>().enabled=true;
            Cursor.lockState=CursorLockMode.Locked;Cursor.visible=false;
        }
    }
    void OnGUI()
    {
        if(!isPlayer)return;
        if(!Dead && !GetComponent<FirstPersonController>().isActiveAndEnabled)return;
        if(Time.time<hurtUntil) {var color=GUI.color;GUI.color=new Color(1,.1f,.05f,.16f);GUI.DrawTexture(new Rect(0,0,Screen.width,Screen.height),Texture2D.whiteTexture);GUI.color=color;}
        hud.Draw(this,Mathf.Max(0,respawnAt-Time.unscaledTime));
    }
    void OnDestroy(){hud.Dispose();}
}
