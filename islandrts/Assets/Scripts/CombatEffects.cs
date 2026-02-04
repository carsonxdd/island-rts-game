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
            DontDestroyOnLoad(gameObject);
            cachedParticleMaterial = new Material(Shader.Find("Sprites/Default"));
            Debug.Log("CombatEffects: Initialized");
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

    /// <summary>
    /// Spawn attack effect at attacker's position toward target
    /// </summary>
    public void SpawnAttackEffect(Vector3 attackerPosition, Vector3 targetPosition, bool isWarrior)
    {
        if (!enableAttackEffects || particlesThisFrame >= maxParticlesPerFrame)
            return;

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
    }

    /// <summary>
    /// Spawn hit effect at impact position
    /// </summary>
    public void SpawnHitEffect(Vector3 hitPosition, float damage)
    {
        if (!enableHitEffects || particlesThisFrame >= maxParticlesPerFrame)
            return;

        // Create impact particle burst
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
    }

    /// <summary>
    /// Spawn death effect (particle burst)
    /// </summary>
    public void SpawnDeathEffect(Vector3 deathPosition, bool isWarrior)
    {
        if (!enableDeathEffects || particlesThisFrame >= maxParticlesPerFrame)
            return;

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
    }

    /// <summary>
    /// Fade out a unit's renderer on death
    /// </summary>
    public void FadeOutUnit(GameObject unit, float duration)
    {
        if (!enableDeathEffects)
            return;

        Renderer renderer = unit.GetComponent<Renderer>();
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

    public void Initialize(float dur, float speed)
    {
        duration = dur;
        riseSpeed = speed;
        tmp = GetComponent<TextMeshPro>();
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
        if (Camera.main != null)
        {
            transform.LookAt(Camera.main.transform);
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
    private Material material;

    public void Initialize(Renderer rend, float dur)
    {
        targetRenderer = rend;
        duration = dur;

        // Create a copy of the material so we can modify it
        material = new Material(targetRenderer.material);
        targetRenderer.material = material;

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

    void Update()
    {
        timer += Time.deltaTime;
        float alpha = 1f - (timer / duration);

        if (material != null)
        {
            Color color = material.color;
            color.a = alpha;
            material.color = color;
        }

        if (timer >= duration)
        {
            Destroy(this);
        }
    }
}
