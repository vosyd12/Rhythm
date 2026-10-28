using UnityEngine;
using UnityEngine.UI;

public class ForestHoldChargeParticles : MonoBehaviour
{
    [Header("기존 시스템")]
    [SerializeField] private MusicPlayback musicPlayback;
    [SerializeField] private RhythmInputTester rhythmInputTester;

    [Header("이 도끼의 방향")]
    [SerializeField] private RhythmInputSide side;

    [Header("입자 원본")]
    [SerializeField] private Image particleTemplate;

    [Header("충전 연출")]
    [SerializeField, Range(8, 999)]
    private int particleCount = 40;

    [SerializeField, Min(1.0f)]
    private float startRadius = 48.0f;

    [SerializeField, Min(0.1f)]
    private float travelSeconds = 0.7f;

    [Header("입자 크기와 발광")]
    [SerializeField]
    private Vector2 sizeMultiplierRange =
        new Vector2(0.4f, 2.2f);

    [SerializeField, Range(1.0f, 60.0f)]
    private float emissionStrength = 5.0f;

    [Header("중심 발광")]
    [SerializeField, Min(1.0f)]
    private float coreStartSize = 30.0f;

    [SerializeField, Min(1.0f)]
    private float coreEndSize = 100.0f;

    [SerializeField, Range(0.0f, 1.0f)]
    private float coreMaxAlpha = 0.35f;

    [Header("충전 진행에 따른 발광")]
    [SerializeField, Range(0.0f, 1.0f)]
    private float startEmissionRatio = 0.2f;

    private struct Particle
    {
        public Image Image;
        public RectTransform Rect;
        public Vector2 BaseSize;
        public float Speed;
        public float Phase;
    }

    private static readonly int ColorPropertyId =
        Shader.PropertyToID("_Color");

    private Particle[] particles;
    private Material particleMaterial;

    private Image chargeCore;
    private RectTransform coreRect;
    private Color particleColor;

    private bool isInitialized;
    private bool isVisible;

    private int observedPlaybackId = -1;
    private int activeNoteId = -1;

    private double startedAt;
    private float currentCharge;
    private float lastEmission = -1.0f;

    private void Awake()
    {
        ClampSettings();

        if (musicPlayback == null ||
            rhythmInputTester == null ||
            particleTemplate == null)
        {
            FailInitialization(
                "음악, 입력기, 입자 원본을 연결해 주세요.");
            return;
        }

        if (side != RhythmInputSide.Left &&
            side != RhythmInputSide.Right)
        {
            FailInitialization(
                "입자 방향은 Left 또는 Right로 설정해 주세요.");
            return;
        }

        // 원본을 끄면서 이 컴포넌트까지 꺼지는 잘못된 연결을 막는다.
        if (particleTemplate.transform == transform ||
            !particleTemplate.transform.IsChildOf(transform))
        {
            FailInitialization(
                "Particle Template에는 이 ChargeParticles 아래의 " +
                "자식 Image를 연결해 주세요.");
            return;
        }

        Material sourceMaterial = particleTemplate.material;

        if (sourceMaterial == null ||
            !sourceMaterial.HasProperty(ColorPropertyId))
        {
            FailInitialization(
                "입자 원본에 _Color를 지원하는 발광 머티리얼을 " +
                "연결해 주세요.");
            return;
        }

        particleColor = particleTemplate.color;
        Vector2 templateSize =
            particleTemplate.rectTransform.sizeDelta;

        particleTemplate.gameObject.SetActive(false);

        particleMaterial = new Material(sourceMaterial)
        {
            name = "ForestHoldParticles_Runtime"
        };

        particles = new Particle[particleCount];

        // 패턴 생성 등 다른 시스템의 난수 상태에 영향을 주지 않는다.
        System.Random random =
            new System.Random(12345 + (int)side);

        for (int i = 0; i < particles.Length; i++)
        {
            Image image = CreateImage($"ChargeParticle_{i + 1}");

            float sizeRoll = (float)random.NextDouble();

            float sizeBlend = i % 8 == 0
                ? Mathf.Lerp(0.75f, 1.0f, sizeRoll)
                : sizeRoll * sizeRoll * 0.65f;

            float sizeFactor = Mathf.Lerp(
                sizeMultiplierRange.x,
                sizeMultiplierRange.y,
                sizeBlend);

            particles[i] = new Particle
            {
                Image = image,
                Rect = image.rectTransform,
                BaseSize = templateSize * sizeFactor,
                Speed = Mathf.Lerp(
                    0.75f, 1.4f, (float)random.NextDouble()),
                Phase = (float)random.NextDouble()
            };
        }

        chargeCore = CreateImage("ChargeCore");
        coreRect = chargeCore.rectTransform;
        coreRect.anchoredPosition = Vector2.zero;
        coreRect.localRotation = Quaternion.identity;
        coreRect.SetAsFirstSibling();

        observedPlaybackId = musicPlayback.PlaybackId;
        isInitialized = true;

        UpdateChargeEmission();
    }

    private Image CreateImage(string objectName)
    {
        Image image = Instantiate(
            particleTemplate, transform, false);

        image.name = objectName;
        image.raycastTarget = false;
        image.material = particleMaterial;
        image.enabled = true;
        image.gameObject.SetActive(false);

        return image;
    }

    private void LateUpdate()
    {
        if (!isInitialized) { return; }

        // 연결 대상이 실행 중 제거되어도 표시를 남기지 않는다.
        if (musicPlayback == null || rhythmInputTester == null)
        {
            HideParticles();
            enabled = false;
            return;
        }

        if (observedPlaybackId != musicPlayback.PlaybackId)
        {
            observedPlaybackId = musicPlayback.PlaybackId;
            HideParticles();
        }

        RhythmNoteData note = rhythmInputTester.ActiveHoldNote;

        if (!musicPlayback.IsGameplayPlayback ||
            musicPlayback.HasNaturallyEnded ||
            !rhythmInputTester.isActiveAndEnabled ||
            note == null ||
            !note.IsHold ||
            note.Side != side)
        {
            HideParticles();
            return;
        }

        if (!IsFinite(note.TargetTimeSeconds) ||
            !IsFinite(note.DurationSeconds) ||
            note.DurationSeconds <= 0.0)
        {
            HideParticles();
            return;
        }

        if (isVisible && activeNoteId != note.Id)
        {
            HideParticles();
        }

        if (musicPlayback.IsPaused)
        {
            // 움직임은 멈춘 채 Inspector의 발광 조절만 반영한다.
            if (isVisible)
            {
                UpdateChargeEmission();
            }

            return;
        }

        if (!musicPlayback.TryGetSongTime(out double songTime) ||
            !IsFinite(songTime))
        {
            HideParticles();
            return;
        }

        if (!isVisible)
        {
            activeNoteId = note.Id;
            startedAt = songTime;
            SetVisible(true);
        }

        float elapsed = Mathf.Max(
            0.0f, (float)(songTime - startedAt));

        currentCharge = Mathf.Clamp01((float)(
            (songTime - note.TargetTimeSeconds) /
            note.DurationSeconds));

        UpdateChargeEmission();
        UpdateChargeCore(elapsed);
        UpdateParticles(elapsed);
    }

    private void UpdateParticles(float elapsed)
    {
        float cycleTime = elapsed / travelSeconds;
        float chargeScale =
            Mathf.Lerp(0.7f, 1.0f, currentCharge);

        for (int i = 0; i < particles.Length; i++)
        {
            Particle particle = particles[i];

            // 생성한 이미지가 외부에서 제거된 경우 건너뛴다.
            if (particle.Image == null) { continue; }

            float progress = Mathf.Repeat(
                cycleTime * particle.Speed + particle.Phase,
                1.0f);

            float remaining = 1.0f - progress;

            float radius =
                startRadius *
                Mathf.Lerp(0.75f, 1.25f, particle.Phase) *
                remaining * remaining;

            float angle =
                (i * 137.5f + remaining * 100.0f) *
                Mathf.Deg2Rad;

            particle.Rect.anchoredPosition = new Vector2(
                Mathf.Cos(angle),
                Mathf.Sin(angle)) * radius;

            float sparkleWave = 0.5f + 0.5f * Mathf.Sin(
                elapsed * (6.0f + particle.Speed * 5.0f) +
                i * 2.4f);

            float sparkle =
                sparkleWave * sparkleWave * sparkleWave;

            particle.Rect.sizeDelta =
                particle.BaseSize *
                chargeScale *
                Mathf.Lerp(0.85f, 1.15f, sparkle);

            float fadeIn = Mathf.SmoothStep(
                0.0f, 1.0f, progress / 0.12f);

            float fadeOut = 1.0f - Mathf.SmoothStep(
                0.0f, 1.0f, (progress - 0.8f) / 0.2f);

            Color color = Color.Lerp(
                particleColor, Color.white, sparkle * 0.35f);

            color.a =
                particleColor.a *
                fadeIn *
                fadeOut *
                Mathf.Lerp(0.8f, 1.0f, sparkle);

            particle.Image.color = color;
        }
    }

    private void UpdateChargeCore(float elapsed)
    {
        if (chargeCore == null) { return; }

        float buildup = currentCharge * currentCharge;

        float pulse = 0.5f + 0.5f *
            Mathf.Sin(elapsed * Mathf.PI * 2.0f * 3.0f);

        float pulseAmount =
            Mathf.Lerp(0.03f, 0.12f, currentCharge);

        float size = Mathf.Lerp(
            coreStartSize, coreEndSize, buildup);

        size *= 1.0f + pulse * pulseAmount;
        coreRect.sizeDelta = new Vector2(size, size);

        Color color = Color.Lerp(
            particleColor,
            Color.white,
            0.35f + buildup * 0.3f);

        color.a =
            particleColor.a *
            Mathf.Lerp(0.04f, coreMaxAlpha, buildup) *
            Mathf.Lerp(0.8f, 1.0f, pulse);

        chargeCore.color = color;
    }

    private void UpdateChargeEmission()
    {
        float buildup = Mathf.SmoothStep(
            0.0f, 1.0f, currentCharge);

        float brightness = emissionStrength * Mathf.Lerp(
            startEmissionRatio, 1.0f, buildup);

        // 일시정지·충전 완료 등 밝기가 같은 동안에는 재설정하지 않는다.
        if (brightness == lastEmission) { return; }

        particleMaterial.SetColor(
            ColorPropertyId,
            new Color(brightness, brightness, brightness, 1.0f));

        lastEmission = brightness;
    }

    private void SetVisible(bool visible)
    {
        if (isVisible == visible) { return; }

        isVisible = visible;

        if (particles != null)
        {
            foreach (Particle particle in particles)
            {
                if (particle.Image != null)
                {
                    particle.Image.gameObject.SetActive(visible);
                }
            }
        }

        if (chargeCore != null)
        {
            chargeCore.gameObject.SetActive(visible);
        }
    }

    private void HideParticles()
    {
        currentCharge = 0.0f;
        activeNoteId = -1;
        SetVisible(false);
    }

    private void OnDisable()
    {
        HideParticles();
    }

    private void OnDestroy()
    {
        // 컴포넌트만 제거해도 생성한 오브젝트가 남지 않도록 정리한다.
        if (particles != null)
        {
            foreach (Particle particle in particles)
            {
                if (particle.Image != null)
                {
                    Destroy(particle.Image.gameObject);
                }
            }
        }

        if (chargeCore != null)
        {
            Destroy(chargeCore.gameObject);
        }

        if (particleMaterial != null)
        {
            Destroy(particleMaterial);
        }
    }

    private void FailInitialization(string message)
    {
        Debug.LogError(message, this);
        enabled = false;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) &&
               !double.IsInfinity(value);
    }

    private void OnValidate()
    {
        ClampSettings();
    }

    private void ClampSettings()
    {
        particleCount = Mathf.Clamp(particleCount, 8, 999);
        startRadius = Mathf.Max(1.0f, startRadius);
        travelSeconds = Mathf.Max(0.1f, travelSeconds);

        sizeMultiplierRange.x =
            Mathf.Max(0.1f, sizeMultiplierRange.x);

        sizeMultiplierRange.y =
            Mathf.Max(sizeMultiplierRange.x, sizeMultiplierRange.y);

        emissionStrength = Mathf.Clamp(emissionStrength, 1.0f, 60.0f);
        startEmissionRatio = Mathf.Clamp01(startEmissionRatio);

        coreStartSize = Mathf.Max(1.0f, coreStartSize);
        coreEndSize = Mathf.Max(coreStartSize, coreEndSize);
        coreMaxAlpha = Mathf.Clamp01(coreMaxAlpha);
    }
}