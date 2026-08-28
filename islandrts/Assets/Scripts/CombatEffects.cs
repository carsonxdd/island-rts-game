using UnityEngine;
using TMPro;

/// <summary>
/// Manages all combat visual effects (particles, damage numbers, screen shake)
/// Singleton pattern for easy access from combat scripts
/// </summary>
public class CombatEffects : MonoBehaviour
{
    public static CombatEffects Instance { get; private set; }

    [Header("Attack Effects")]
    public bool enableAttackEffects = true;
    public Color warriorAttackColor = new Color(0.3f, 0.5f, 1f); // Blue
    public Color enemyAttackColor = new Color(1f, 0.3f, 0.2f); // Red

    [Header("Hit Effects")]
    public bool enableHitEffects = true;
    public bool showDamageNumbers = true;
    public float damageNumberDuration = 1f;
    public float damageNumberRiseSpeed = 2f;

    [Header("Death Effects")]
    public bool enableDeathEffects = true;
    public float deathFadeDuration = 0.5f;

    [Header("Performance")]
    public int maxParticlesPerFrame = 10;

    private int particlesThisFrame = 0;
    private Material cachedParticleMaterial;

    void Awake()
    {
        // Singleton pattern
        if (Instance == null)
        {
            Instance = this;
            cachedParticleMaterial = new Material(Shader.Find("Sprites/Default"));
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void LateUpdate()
    {
        // Reset particle counter each frame
        particlesThisFrame = 0;
    }

    // --- Perf instrumentation ---------------------------------------------
    // Creating a ParticleSystem or a TextMeshPro object at runtime is one of the
    // more expensive things this game does per combat hit, so the cost is timed
    // and attributed rather than guessed at.
    private long vfxT0;
    void VfxBegin() { vfxT0 = System.Diagnostics.Stopwatch.GetTimestamp(); }
    void VfxEnd()
    {
        PerfCounters.VfxTicks += System.Diagnostics.Stopwatch.GetTimestamp() - vfxT0;
        PerfCounters.Hit(PerfCounters.K.VfxSpawn);
    }

    /// <summary>
    /// Spawn attack effect at attacker's position toward target
    /// </summary>
    public void SpawnAttackEffect(Vector3 attackerPosition, Vector3 targetPosition, bool isWarrior)
    {
        if (!enableAttackEffects || particlesThisFrame >= maxParticlesPerFrame)
            return;

        VfxBegin();
        GameObject effectObj = new GameObject("AttackEffect");
        effectObj.transform.position = attackerPosition + Vector3.up * 1f;

        // Create particle system
        ParticleSystem ps = effectObj.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.startLifetime = 0.5f;
        main.startSpeed = 5f;
        main.startSize = 0.3f;
        main.startColor = isWarrior ? warriorAttackColor : enemyAttackColor;
        main.maxParticles = 20;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        // Shape - cone toward target
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 15f;
        shape.radius = 0.1f;

        // Point toward target
        Vector3 direction = (targetPosition - attackerPosition).normalized;
        effectObj.transform.rotation = Quaternion.LookRotation(direction);

        // Emission
        var emission = ps.emission;
        emission.rateOverTime = 0;
        emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 15) });

        // Renderer
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.material = cachedParticleMaterial;

        // Auto-destroy after particles finish
        Destroy(effectObj, 2f);
        particlesThisFrame++;
        VfxEnd();
    }

    /// <summary>
    /// Spawn hit effect at impact position
    /// </summary>
    public void SpawnHitEffect(Vector3 hitPosition, float damage)
    {
        if (!enableHitEffects || particlesThisFrame >= maxParticlesPerFrame)
            return;

        // Create impact particle burst
        VfxBegin();
        GameObject effectObj = new GameObject("HitEffect");
        effectObj.transform.position = hitPosition + Vector3.up * 1f;

        ParticleSystem ps = effectObj.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.startLifetime = 0.3f;
        main.startSpeed = 2f;
        main.startSize = 0.2f;
        main.startColor = new Color(1f, 0.8f, 0.2f); // Yellow/orange flash
        main.maxParticles = 15;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.5f;

        var emission = ps.emission;
        emission.rateOverTime = 0;
        emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 10) });

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.material = cachedParticleMaterial;

        Destroy(effectObj, 1f);
        particlesThisFrame++;
        VfxEnd();

        // Spawn damage number
        if (showDamageNumbers)
        {
            SpawnDamageNumber(hitPosition, damage);
        }
    }

    /// <summary>
    /// Spawn floating damage number
    /// </summary>
    void SpawnDamageNumber(Vector3 position, float damage)
    {
        VfxBegin();
        GameObject textObj = new GameObject("DamageNumber");
        textObj.transform.position = position + Vector3.up * 1.5f;

        TextMeshPro tmp = textObj.AddComponent<TextMeshPro>();
        tmp.text = $"-{damage:F0}";
        tmp.fontSize = 3f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.red;
        tmp.fontStyle = FontStyles.Bold;

        // Make it render on top
        tmp.GetComponent<MeshRenderer>().sortingOrder = 200;

        // Add component to make it rise and fade
        textObj.AddComponent<DamageNumberAnimator>().Initialize(damageNumberDuration, damageNumberRiseSpeed);

        Destroy(textObj, damageNumberDuration);
        VfxEnd();
    }

    /// <summary>
    /// Spawn death effect (particle burst)
    /// </summary>
    public void SpawnDeathEffect(Vector3 deathPosition, bool isWarrior)
    {
        if (!enableDeathEffects || particlesThisFrame >= maxParticlesPerFrame)
            return;

        VfxBegin();
        GameObject effectObj = new GameObject("DeathEffect");
        effectObj.transform.position = deathPosition + Vector3.up * 0.5f;

        ParticleSystem ps = effectObj.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.startLifetime = 1f;
        main.startSpeed = 3f;
        main.startSize = 0.4f;
        main.startColor = isWarrior ? new Color(0.5f, 0.5f, 1f) : new Color(1f, 0.3f, 0.3f);
        main.maxParticles = 30;
        main.gravityModifier = 0.5f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.5f;

        var emission = ps.emission;
        emission.rateOverTime = 0;
        emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, 25) });

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.material = cachedParticleMaterial;

        Destroy(effectObj, 2f);
        particlesThisFrame++;
        VfxEnd();
    }

    /// <summary>
    /// Fade out a unit's renderer on death
    /// </summary>
    public void FadeOutUnit(GameObject unit, float duration)
    {
        if (!enableDeathEffects)
            return;

        // GetComponentInChildren, not GetComponent: low-poly art lives on a "Model" child,
        // so the unit root no longer has a Renderer of its own.
        Renderer renderer = unit.GetComponentInChildren<Renderer>();
        if (renderer != null)
        {
            unit.AddComponent<FadeOutEffect>().Initialize(renderer, duration);
        }
    }
}

/// <summary>
/// Helper component to animate damage numbers
/// </summary>
public class DamageNumberAnimator : MonoBehaviour
{
    private float duration;
    private float riseSpeed;
    private float timer = 0f;
    private TextMeshPro tmp;
    private Transform cachedCameraTransform;

    public void Initialize(float dur, float speed)
    {
        duration = dur;
        riseSpeed = speed;
        tmp = GetComponent<TextMeshPro>();

        Camera mainCamera = Camera.main;
        cachedCameraTransform = mainCamera != null ? mainCamera.transform : null;
    }

    void Update()
    {
        timer += Time.deltaTime;

        // Rise up
        transform.position += Vector3.up * riseSpeed * Time.deltaTime;

        // Fade out
        if (tmp != null)
        {
            float alpha = 1f - (timer / duration);
            tmp.color = new Color(tmp.color.r, tmp.color.g, tmp.color.b, alpha);
        }

        // Billboard effect
        if (cachedCameraTransform != null)
        {
            transform.LookAt(cachedCameraTransform);
            transform.Rotate(0, 180, 0);
        }
    }
}

/// <summary>
/// Helper component to fade out renderer
/// </summary>
public class FadeOutEffect : MonoBehaviour
{
    private Renderer targetRenderer;
    private float duration;
    private float timer = 0f;
    private Material[] materials;

    public void Initialize(Renderer rend, float dur)
    {
        targetRenderer = rend;
        duration = dur;

        // Every slot, not just slot 0 - the low-poly art meshes are multi-submesh (8 materials
        // on a unit), so fading only .material left 7/8 of the body opaque. Reading .materials
        // instantiates per-renderer copies, which is what we want since we mutate them.
        materials = targetRenderer.materials;

        for (int i = 0; i < materials.Length; i++)
        {
            Material material = materials[i];
            if (material == null) continue;

            // Enable transparency
            material.SetFloat("_Mode", 2); // Fade mode
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = 3000;
        }
    }

    void Update()
    {
        timer += Time.deltaTime;
        float alpha = 1f - (timer / duration);

        if (materials != null)
        {
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null) continue;
                Color color = materials[i].color;
                color.a = alpha;
                materials[i].color = color;
            }
        }

        if (timer >= duration)
        {
            Destroy(this);
        }
    }
}
