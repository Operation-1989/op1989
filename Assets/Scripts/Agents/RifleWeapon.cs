using System;
using UnityEngine;

/// <summary>Shared hitscan rifle. All shots stop at the nearest solid surface.</summary>
public sealed class RifleWeapon : MonoBehaviour
{
    public Transform muzzle;
    public AudioClip shotSound;
    public float damage = 25, range = 100;
    public bool playerWeapon;
    public int ShotsFired { get; private set; }
    AudioSource audioSource;
    LineRenderer tracer, flash;
    Light muzzleLight;
    Material effectMaterial;
    float visibleUntil;

    void Awake()
    {
        audioSource=gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake=false; audioSource.spatialBlend=playerWeapon?0:1;
        audioSource.minDistance=4; audioSource.maxDistance=90; audioSource.rolloffMode=AudioRolloffMode.Linear;
        effectMaterial=new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
        effectMaterial.SetColor("_BaseColor",new Color(3,1.5f,.35f));
        tracer=MakeLine("Rifle tracer",.018f); flash=MakeLine("Muzzle flash",.09f);
        var lightObject=new GameObject("Muzzle light"); lightObject.transform.SetParent(transform,false);
        muzzleLight=lightObject.AddComponent<Light>(); muzzleLight.color=new Color(1,.65f,.2f);
        muzzleLight.range=3; muzzleLight.intensity=3; muzzleLight.enabled=false;
    }
    LineRenderer MakeLine(string label,float width)
    {
        var go=new GameObject(label);go.transform.SetParent(transform,false);
        var line=go.AddComponent<LineRenderer>();line.sharedMaterial=effectMaterial;
        line.positionCount=2;line.startWidth=width;line.endWidth=width*.15f;line.enabled=false;
        line.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
        return line;
    }
    public static bool Trace(Vector3 origin,Vector3 direction,float distance,Transform owner,out RaycastHit hit)
    {
        var hits=Physics.RaycastAll(origin,direction,distance,~0,QueryTriggerInteraction.Ignore);
        Array.Sort(hits,(a,b)=>a.distance.CompareTo(b.distance));
        foreach(var candidate in hits)
            if(candidate.transform!=owner && !candidate.transform.IsChildOf(owner)) {hit=candidate;return true;}
        hit=default;return false;
    }
    public void Shoot(Vector3 origin, Vector3 direction)
    {
        if(!isActiveAndEnabled || direction.sqrMagnitude<.001f)return;
        direction.Normalize();
        Vector3 start=muzzle?muzzle.position:origin;
        Vector3 end=origin+direction*range;
        if(Trace(origin,direction,range,transform,out var hit))
        {
            end=hit.point;
            // A camera can see over cover while the barrel remains behind it.
            if(Trace(start,(end-start).normalized,Vector3.Distance(start,end),transform,out var barrelHit))
            {hit=barrelHit;end=hit.point;}
            var health=hit.collider.GetComponentInParent<CombatHealth>();
            if(health)health.TakeDamage(damage);
        }
        else if(Trace(start,(end-start).normalized,Vector3.Distance(start,end),transform,out var barrelHit))
        {
            end=barrelHit.point;var health=barrelHit.collider.GetComponentInParent<CombatHealth>();
            if(health)health.TakeDamage(damage);
        }
        ShotsFired++;
        if(shotSound) {audioSource.pitch=UnityEngine.Random.Range(.95f,1.05f);audioSource.PlayOneShot(shotSound,playerWeapon?.55f:.7f);}
        tracer.SetPosition(0,start);tracer.SetPosition(1,end);tracer.enabled=true;
        flash.SetPosition(0,start);flash.SetPosition(1,start+direction*.38f);flash.enabled=true;
        muzzleLight.transform.position=start;muzzleLight.enabled=true;visibleUntil=Time.time+.045f;
    }
    void Update(){if(Time.time>=visibleUntil)Hide();}
    void Hide(){if(tracer)tracer.enabled=false;if(flash)flash.enabled=false;if(muzzleLight)muzzleLight.enabled=false;}
    void OnDisable(){Hide();}
    void OnDestroy(){if(effectMaterial)Destroy(effectMaterial);}
}
