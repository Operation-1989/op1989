using UnityEngine;

[RequireComponent(typeof(CharacterController),typeof(RifleWeapon),typeof(CombatHealth))]
public sealed class BarricadePatrolSoldier : MonoBehaviour
{
    public Transform player;
    public Transform[] waypoints;
    public Animator animator;
    public float detectionRange=32, fieldOfView=120, walkSpeed=1.5f, runSpeed=3.5f;
    public float maximumAdvance=4, minimumDistance=14, shotInterval=.18f;
    public string State {get;private set;}="Patrol";
    public float AdvancedDistance {get;private set;}
    CharacterController controller;
    RifleWeapon rifle;
    CombatHealth targetHealth;
    int waypoint,burst;
    float lastSeen=-100,waitUntil,nextShot,advanceUntil;
    Vector3 previous;

    void Awake()
    {
        controller=GetComponent<CharacterController>();rifle=GetComponent<RifleWeapon>();
        if(player)targetHealth=player.GetComponent<CombatHealth>();previous=transform.position;
    }
    public void ResetPatrol(){State="Patrol";AdvancedDistance=0;waypoint=burst=0;lastSeen=-100;waitUntil=nextShot=0;previous=transform.position;}
    public void Alert()
    {
        if(State=="Patrol") {State="Advance";AdvancedDistance=0;advanceUntil=Time.time+1.25f;}
        lastSeen=Time.time;
    }
    Vector3 Target=>player.position+Vector3.up*.45f;
    bool Visible(bool checkAngle)
    {
        Vector3 eye=transform.position+Vector3.up*1.55f,delta=Target-eye;
        if(delta.magnitude>detectionRange)return false;
        if(checkAngle && Vector3.Angle(transform.forward,delta)>fieldOfView*.5f)return false;
        return !RifleWeapon.Trace(eye,delta.normalized,delta.magnitude,transform,out var hit) || hit.transform==player || hit.transform.IsChildOf(player);
    }
    void Update()
    {
        if(!player)return;
        bool active=player.GetComponent<FirstPersonController>().isActiveAndEnabled && (!targetHealth || !targetHealth.Dead);
        bool visible=active && Visible(State=="Patrol");
        if(visible)Alert();
        Vector3 move=Vector3.zero;
        if(State=="Patrol")
        {
            if(waypoints!=null && waypoints.Length>0 && waypoints[waypoint])
            {
                var delta=waypoints[waypoint].position-transform.position;delta.y=0;
                if(delta.magnitude<.3f){if(waitUntil==0)waitUntil=Time.time+1;if(Time.time>=waitUntil){waypoint=(waypoint+1)%waypoints.Length;waitUntil=0;}}
                else {move=delta.normalized*Mathf.Min(walkSpeed,delta.magnitude/Mathf.Max(Time.deltaTime,.001f));Face(delta);}
            }
        }
        else
        {
            var delta=player.position-transform.position;delta.y=0;Face(delta);
            if(State=="Advance")
            {
                float budget=Mathf.Min(maximumAdvance-AdvancedDistance,delta.magnitude-minimumDistance);
                if(visible && budget>.05f && Time.time<advanceUntil)
                    move=delta.normalized*Mathf.Min(runSpeed,budget/Mathf.Max(Time.deltaTime,.001f));
                else State="Fire";
            }
            if(State=="Fire" && visible && Time.time>=nextShot && Vector3.Angle(transform.forward,delta)<12)
            {
                Vector3 origin=rifle.muzzle?rifle.muzzle.position:transform.position+Vector3.up*1.4f;
                Vector3 aim=Target+Random.insideUnitSphere*.6f;
                rifle.Shoot(origin,(aim-origin).normalized);animator.SetTrigger("Fire");
                burst++;nextShot=Time.time+(burst%3==0?1.1f:shotInterval);
            }
            if(Time.time-lastSeen>5 || !active) {State="Patrol";waitUntil=0;}
        }
        controller.Move((move+Vector3.down*2)*Time.deltaTime);
        Vector3 movement=transform.position-previous;movement.y=0;
        if(State=="Advance")AdvancedDistance+=movement.magnitude;
        if(animator)animator.SetFloat("Speed",movement.magnitude>.001f?(State=="Advance"?2:1):0,.12f,Time.deltaTime);
        previous=transform.position;
    }
    void Face(Vector3 direction){if(direction.sqrMagnitude>.01f)transform.rotation=Quaternion.RotateTowards(transform.rotation,Quaternion.LookRotation(direction),240*Time.deltaTime);}
    void OnDrawGizmosSelected()
    {
        if(waypoints==null)return;Gizmos.color=Color.cyan;
        for(int i=0;i<waypoints.Length;i++)if(waypoints[i] && waypoints[(i+1)%waypoints.Length])
        {Gizmos.DrawSphere(waypoints[i].position,.2f);Gizmos.DrawLine(waypoints[i].position,waypoints[(i+1)%waypoints.Length].position);}
    }
}
